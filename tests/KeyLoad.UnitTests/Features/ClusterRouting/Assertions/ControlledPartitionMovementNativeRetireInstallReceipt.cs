using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireInstallReceipt
{
    internal static async Task RequireAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMoveRequest request, CommitToken actual, CancellationToken cancellationToken)
    {
        var target = owners.Target;
        var work = new ReadExecutionBudget(target.Runtime.Core.DatabaseLimits, target.Clock, cancellationToken);
        work.Check();
        await Assert.That(actual.Position <= target.Log.State.CommittedIndex).IsTrue();
        var entry = target.Log.ReadEntry(actual.Position, work.ChargeBytes)
            ?? throw new InvalidOperationException("The genuine installed receipt has no committed entry.");
        var operation = entry.Operation
            ?? throw new InvalidOperationException("The genuine installed entry has no operation.");
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Install);
        await Assert.That(phase.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(operation.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
        await Assert.That(operation.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(request,
            PhysicalShardCatalogFixture.RootPrincipalId, PartitionMovementParentPhaseRole.Install, phase.PageOrdinal));
        await Assert.That(entry.Term).IsGreaterThan(0L);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(new CommitToken(
            target.Store.Identity.Incarnation, request.Partition.AtomicPartitionId, entry.Index,
            owners.Destination.Owner.PlacementEpoch)))).IsTrue();
        work.Check();
    }
}
