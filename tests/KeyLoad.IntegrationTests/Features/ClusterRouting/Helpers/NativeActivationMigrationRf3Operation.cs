using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationMigrationRf3Operation
{
    private const int EmptyFailureCount = 0;
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity, CommandRequest command,
        RequestCqrsProbeActivationRecord established, CommitReceipt receipt,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var placement = await NativeActivationMigrationRf3Ownership.ReadPlacementAsync(administrator, identity.Partition, cancellationToken);
        var physical = await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, cancellationToken);
        var target = discovery.First(item => item.VoterId != established.Voter);
        _ = await NativeActivationMigrationRf3Attempt.RunAsync(wave, callers, identity, command, established, receipt,
            discovery, target, cancel: false, foreign: true, failures, cancellationToken);
        await RequireStateAsync(wave, administrator, callers, identity, placement, physical, cancellationToken);
        _ = await NativeActivationMigrationRf3Attempt.RunAsync(wave, callers, identity, command, established, receipt,
            discovery, target, cancel: true, foreign: false, failures, cancellationToken);
        await RequireStateAsync(wave, administrator, callers, identity, placement, physical, cancellationToken);
        var before = await NativeActivationMigrationRf3Attempt.RunAsync(wave, callers, identity, command, established,
            receipt, discovery, target, cancel: false, foreign: false, failures, cancellationToken);
        var after = await NativeActivationRf3HeldCall.ExecuteAsync(wave, callers, identity, command,
            useMcp: true, discovery, cancellationToken).ConfigureAwait(false);
        await NativeActivationRf3Witness.RequireReplacementAsync(before, after.Witness);
        await Assert.That(after.Witness.Voter).IsEqualTo(target.VoterId);
        await Assert.That(after.Witness.SiloAddress).IsEqualTo(target.SiloAddress);
        await SqlRf3Protocol.EqualAsync(receipt, after.Receipt);
        await RequireStateAsync(wave, administrator, callers, identity, placement, physical, cancellationToken);
        if (failures.Count != EmptyFailureCount)
        { ServerFailureObserver.ThrowIfAny(failures); }
        await NativeActivationRf3ColdOutcome.VerifyAsync(wave, identity, command, receipt, cancellationToken);
        await RequireStateAsync(wave, administrator, callers, identity, placement, physical, cancellationToken);
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(callers, administrator.Sdk, reference, command,
            receipt, RequestCqrsRf3Protocol.DocumentJson, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, cancellationToken);
    }

    private static async Task RequireStateAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity, AtomicPartitionPlacementResolution placement,
        NodeStatus[] physical, CancellationToken cancellationToken)
    {
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(callers, identity,
            RequestCqrsRf3Protocol.ChangedDocumentJson, NativeActivationRf3Protocol.CommittedRevision, cancellationToken);
        await NativeActivationMigrationRf3Ownership.RequirePlacementAsync(administrator, identity.Partition, placement, cancellationToken);
        await NativeActivationMigrationRf3Ownership.RequirePhysicalAsync(physical,
            await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, cancellationToken));
    }
}
