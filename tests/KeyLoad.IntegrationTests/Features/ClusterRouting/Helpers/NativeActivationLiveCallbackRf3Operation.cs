using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationLiveCallbackRf3Operation
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity, CommandRequest command,
        RequestCqrsProbeActivationRecord established, CommitReceipt receipt,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, List<Exception> failures, CancellationToken cancellationToken)
    {
        await using (var held = new NativeActivationHeldProducer<Result<CommitReceipt>>(wave.QueryControls, discovery,
            identity.PrincipalId, command.CommandId, established.Voter, token => callers.Sdk.CommitAsync(command, token), failures, cancellationToken))
        {
            var actual = await held.ObserveAsync(cancellationToken);
            await Assert.That(actual.GrainDigest).IsEqualTo(established.GrainDigest);
            await Assert.That(actual.ActivationId).IsEqualTo(established.ActivationId);
            var before = await NativeActivationRf3Live.ChallengeAsync(wave.QueryControls, actual,
                RequestCqrsProbeLiveProtocol.BeforeReplacement, RequestCqrsProbeLiveProtocol.EmptyDigest, cancellationToken);
            var after = await NativeActivationRf3Live.ChallengeAsync(wave.QueryControls, actual,
                RequestCqrsProbeLiveProtocol.AfterReplacement, NativeActivationRf3Live.Digest(wave.QueryControls, before), cancellationToken);
            await Assert.That(after.Nonce).IsNotEqualTo(before.Nonce);
            await Assert.That(after.ActivationId).IsEqualTo(actual.ActivationId);
            await Assert.That(held.Producer.IsCompleted).IsFalse();
            held.Release();
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await held.Producer.ConfigureAwait(false)));
        }
        await NativeActivationRf3ColdOutcome.VerifyAsync(wave, identity, command, receipt, cancellationToken);
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(callers, administrator.Sdk, reference, command,
            receipt, RequestCqrsRf3Protocol.DocumentJson, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, cancellationToken);
    }
}
