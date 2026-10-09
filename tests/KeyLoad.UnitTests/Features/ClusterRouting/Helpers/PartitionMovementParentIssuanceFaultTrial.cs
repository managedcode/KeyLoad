using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns deliberate current-format phase epoch corruption and joins exact restoration before healthy observation.</summary>
internal static class PartitionMovementParentIssuanceFaultTrial
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        PartitionMovementParentNativeFixture fixture, PartitionMoveRequest request, Guid phaseId, string deniedPrincipalId)
    {
        var key = PartitionMoveParentKeys.Phase(request.Partition, request.MoveId, phaseId);
        var retained = source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The real admitted parent phase is absent.");
        var phase = NativeSerialization.Deserialize<PartitionMoveParentPhase>(retained);
        source.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, NativeSerialization.Serialize(phase with
            { OriginalIssuancePolicyEpoch = checked(phase.OriginalIssuancePolicyEpoch + 1) }));
            return true;
        });
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            source.Reopen();
            await PartitionMovementParentPolicyRestoreTrial.DeniedReadAsync(source, target,
                () => fixture.Read(request, phaseId, deniedPrincipalId), ErrorCode.PermissionDenied);
            await PartitionMovementParentPolicyRestoreTrial.DeniedReadAsync(source, target,
                () => fixture.Read(request, phaseId), ErrorCode.Corruption);
        }, failures);
        ServerFailureObserver.Observe(() => source.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, retained);
            return true;
        }), failures);
        ServerFailureObserver.Observe(source.Reopen, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var cold = fixture.Read(request, phaseId);
        await Assert.That(cold.Pending!.OriginalIssuancePolicyEpoch).IsEqualTo(phase.OriginalIssuancePolicyEpoch);
        await Assert.That(cold.CancellationOutcome).IsNotNull();
        await Assert.That(cold.Header!.TerminalResult).IsNull();
    }
}
