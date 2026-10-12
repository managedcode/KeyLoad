using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeHeldOperation
{
    internal static async Task<(NativeCanonicalModelCutRequest Scope, RemoteModelNativeRequest Request,
        RemoteTransferPostAwaitNativeCut Committed)> ExecuteAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeAdministrator administrator, RemoteTransferNativeCallers calls,
        RemoteTransferColdSeed seed, CommandRequest accept, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        var hold = new RemoteTransferNativeResponseHold(wave.QueryControls, seed.Identity.Principal.Id,
            accept.CommandId, accept.Partition);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = calls.Sdk.CommitAsync(accept, operation.Token);
        var failures = new List<Exception>();
        Guid? observedArm = null;
        (NativeCanonicalModelCutRequest Scope, RemoteModelNativeRequest Request,
            RemoteTransferPostAwaitNativeCut Committed)? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var witness = await hold.WaitAsync(original, membership.Source, token);
            observedArm = witness.ArmId;
            var request = RequestCqrsNativeModelInventory.Request(witness);
            var reply = RequestCqrsNativeModelInventory.Reply(witness);
            RemoteTransferNativeGenerationAssertions.Require(request, membership.Source, membership.Destination);
            var scope = Scope(seed, accept, witness);
            var before = await NativeCanonicalModelCutClient.ReadSixAsync(wave, wave.Profile.AdminKey,
                scope, membership.Owners, membership.Secrets, token);
            await NativeCanonicalModelCutRejectionAssertions.RequireAsync(before, token);
            await NativeCanonicalModelCutAdmissionAssertions.RequireAsync(wave, wave.Profile.AdminKey,
                scope, membership.Owners, membership.Secrets, before, token);
            var committed = RemoteTransferNativeCommittedCut.Read(before[TwoRf3MembershipProtocol.Nodes[3]], seed, accept, request);
            var source = RemoteTransferNativeCommittedCut.ReadSource(before[TwoRf3MembershipProtocol.Nodes[0]], seed, accept);
            await RemoteTransferNativeReplyAssertions.RequireAsync(request, reply, membership.SourceSecret,
                membership.DestinationSecret, committed);
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(committed.OriginalReceipt, accept,
                seed.Scenario.DestinationQueue, seed.TransferId, RemoteTransferColdProtocol.AcceptKind,
                committed.OriginalReceipt.Token.Position);
            var revoked = MessagingRf3Identity.WithCapability(seed.Identity.Principal, seed.Scenario.DestinationQueue,
                RemoteTransferColdProtocol.Target & ~Capability.QueuePublish);
            await SqlRf3Protocol.EqualAsync(revoked, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
            await RequireUnchangedAsync(wave, membership, scope, before, token);
            hold.Release();
            await QueueProducerRf3Assertions.DeniedAsync(await original, ErrorCode.UnknownWriteOutcome);
            await RequireUnchangedAsync(wave, membership, scope, before, token);
            var restored = MessagingRf3Identity.WithCapability(revoked, seed.Scenario.DestinationQueue, RemoteTransferColdProtocol.Target);
            await SqlRf3Protocol.EqualAsync(restored, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token)));
            await RemoteTransferNativeReceiptAuthorityAssertions.RequireAsync(wave, administrator, calls,
                seed, restored, scope, before, committed.OriginalReceipt, token);
            var after = await RequireUnchangedAsync(wave, membership, scope, before, token);
            await Assert.That(RemoteTransferNativeCommittedCut.ReadSource(after[TwoRf3MembershipProtocol.Nodes[0]], seed, accept)).IsEqualTo(source);
            result = (scope, request, committed);
        }, failures);
        await JoinOriginalAsync(wave, membership, hold, original, operation, observedArm, failures);
        if (failures.Count != 0) { wave.RetainRoots(); }
        ServerFailureObserver.ThrowIfAny(failures);
        var actual = result ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        await calls.ReplayAsync(accept, actual.Committed.OriginalReceipt, token);
        return actual;
    }

    private static async Task JoinOriginalAsync(TwoRf3MembershipWave wave, RemoteTransferNativeMembership membership,
        RemoteTransferNativeResponseHold hold, Task original, CancellationTokenSource operation, Guid? observedArm,
        List<Exception> failures)
    {
        var beforeRelease = failures.Count;
        ServerFailureObserver.Observe(hold.Release, failures);
        if (failures.Count != beforeRelease)
        { ServerFailureObserver.Observe(operation.Cancel, failures); }
        await ServerFailureObserver.ObserveAsync(async () => { await original; }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        if (observedArm is { } arm)
        { await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.WaitForSettlementAsync(arm, membership.Source, cleanup.Token), failures); }
        else
        { await ServerFailureObserver.ObserveAsync(() => hold.JoinAsync(membership.Source, cleanup.Token), failures); }
    }

    private static NativeCanonicalModelCutRequest Scope(RemoteTransferColdSeed seed, CommandRequest accept,
        RequestCqrsNativeModelWitness actual)
        => new(Guid.NewGuid(), actual.ArmId, actual.SessionId, seed.Scenario.SourcePartition,
            seed.Scenario.DestinationPartition, accept.CommandId, seed.Identity.Principal.Id,
            RemoteTransferDistinctProtocol.TechnicalSubject, seed.Scenario.SourceQueue, seed.TransferId,
            seed.Scenario.DestinationQueue);

    private static async Task<IReadOnlyDictionary<string, NativeCanonicalModelCutWitness>> RequireUnchangedAsync(
        TwoRf3MembershipWave wave, RemoteTransferNativeMembership membership, NativeCanonicalModelCutRequest scope,
        IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before, CancellationToken token)
    {
        var after = await NativeCanonicalModelCutClient.ReadSixAsync(wave, wave.Profile.AdminKey,
            scope, membership.Owners, membership.Secrets, token);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { NativeCanonicalModelCutAssertions.RequireUnchanged(before[node], after[node], token); }
        return after;
    }
}
