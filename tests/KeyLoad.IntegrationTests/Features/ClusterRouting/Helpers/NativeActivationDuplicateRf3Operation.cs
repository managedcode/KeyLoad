using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationDuplicateRf3Operation
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers original, RequestCqrsPhaseFaultIdentity identity, CommandRequest command,
        RequestCqrsProbeActivationRecord established, CommitReceipt receipt,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, List<Exception> failures, CancellationToken cancellationToken)
    {
        var oldNode = RequestCqrsProbeFixtureProtocol.Nodes.Single(node => RequestCqrsProbeFileNames.OriginForNode(node) == established.Voter);
        var survivor = RequestCqrsProbeFixtureProtocol.Nodes.First(node => node != oldNode);
        await using var replacementCaller = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            survivor, identity.Secret, cancellationToken).ConfigureAwait(false);
        await using (var old = new NativeActivationHeldProducer<Result<CommitReceipt>>(wave.QueryControls, discovery,
            identity.PrincipalId, command.CommandId, established.Voter, token => original.Sdk.CommitAsync(command, token), failures, cancellationToken))
        {
            var before = await old.ObserveAsync(cancellationToken).ConfigureAwait(false);
            await Assert.That(before.GrainDigest).IsEqualTo(established.GrainDigest);
            await Assert.That(before.ActivationId).IsEqualTo(established.ActivationId);
            _ = await NativeActivationRf3Live.ChallengeAsync(wave.QueryControls, before,
                RequestCqrsProbeLiveProtocol.BeforeReplacement, RequestCqrsProbeLiveProtocol.EmptyDigest, cancellationToken);
            var isolation = wave.IsolationOwner ?? throw new InvalidOperationException(NativeActivationRf3Protocol.Missing);
            await isolation.IsolateAsync(oldNode, cancellationToken).ConfigureAwait(false);
            await using var next = new NativeActivationHeldProducer<CommitReceipt>(wave.QueryControls, discovery,
                identity.PrincipalId, command.CommandId, null, async token =>
                    (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await replacementCaller.Mcp.CallAsync(
                        McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false)).ConfigureAwait(false)).Value,
                failures, cancellationToken);
            var after = await next.ObserveAsync(cancellationToken).ConfigureAwait(false);
            await NativeActivationRf3Witness.RequireReplacementAsync(before, after).ConfigureAwait(false);
            var oldAfter = await NativeActivationRf3Live.ChallengeAsync(wave.QueryControls, before,
                RequestCqrsProbeLiveProtocol.AfterReplacement, NativeActivationRf3Live.Digest(wave.QueryControls, after), cancellationToken);
            _ = await NativeActivationRf3Live.ChallengeAsync(wave.QueryControls, after,
                RequestCqrsProbeLiveProtocol.BeforeReplacement, NativeActivationRf3Live.Digest(wave.QueryControls, oldAfter), cancellationToken);
            await Assert.That(old.Producer.IsCompleted).IsFalse();
            await Assert.That(next.Producer.IsCompleted).IsFalse();
            await isolation.VerifyObservedFaultAsync(cancellationToken).ConfigureAwait(false);
            old.Release();
            var refused = await old.Producer.ConfigureAwait(false);
            await Assert.That(refused.IsFailed).IsTrue();
            await Assert.That(refused.Value).IsNull();
            await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
            await Assert.That(refused.Problem?.Detail is ReplicaProtocol.NoLeader
                or ReplicaTransportProtocol.TransportUnavailable).IsTrue();
            next.Release();
            await SqlRf3Protocol.EqualAsync(receipt, await next.Producer.ConfigureAwait(false));
        }
        await (wave.IsolationOwner ?? throw new InvalidOperationException(NativeActivationRf3Protocol.Missing))
            .RestoreAsync(cancellationToken).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(original, identity,
            RequestCqrsRf3Protocol.ChangedDocumentJson, NativeActivationRf3Protocol.CommittedRevision, cancellationToken);
        await NativeActivationRf3ColdOutcome.VerifyAsync(wave, identity, command, receipt, cancellationToken).ConfigureAwait(false);
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(original, administrator.Sdk, reference, command,
            receipt, RequestCqrsRf3Protocol.DocumentJson, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, cancellationToken).ConfigureAwait(false);
    }
}
