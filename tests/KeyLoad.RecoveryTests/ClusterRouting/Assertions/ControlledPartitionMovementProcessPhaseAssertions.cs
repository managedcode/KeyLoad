using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Independent complete literal prepared and recovered source-fence values.</summary>
internal static class ControlledPartitionMovementProcessPhaseAssertions
{
    private const int Version = 1;
    private const long InitialEpoch = 1;
    private const long Empty = 0;
    private const long PrepareIndex = 11;
    private const long FenceIndex = 13;
    private const string Principal = "root";

    internal static PartitionMovePhaseResult Fence(ControlledPartitionMovementProcessOwners owners)
    {
        var placement = new AtomicPartitionPlacementResolution(Version,
            ControlledPartitionMovementProcessLiteralCorpus.Partition, owners.Control.Owner.PhysicalShardId,
            owners.Control.Owner.Incarnation, owners.Control.Owner.VoterIds, InitialEpoch, Empty, Empty, true);
        var control = new PartitionMoveControlRecord(Version,
            ControlledPartitionMovementProcessPrepareRequest.MoveId,
            ControlledPartitionMovementProcessLiteralCorpus.Partition, Principal, InitialEpoch, placement,
            owners.Destination.Owner, PartitionMovePhase.Prepared, Empty, PrepareIndex, null, null, null);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(new PartitionMoveIntent(
            Version, control.MoveId, control.Partition, Principal, InitialEpoch, placement,
            owners.Destination.Owner, PrepareIndex))));
        var fence = new PartitionMoveSourceFenceRecord(Version, control.MoveId, control.Partition,
            owners.Control.Owner, placement, owners.Destination.Owner, FenceIndex, digest);
        return new(control.MoveId, PartitionMovePeerStage.Fence,
            new(ControlledPartitionMovementProcessFenceRequest.FenceCommandId, owners.Control.Owner, FenceIndex, digest),
            control, fence, null, null);
    }

    internal static async Task CompleteAsync(OperationResult actual, ControlledPartitionMovementProcessOwners owners)
    {
        await Assert.That(actual.Error).IsNull();
        await Assert.That(actual.SafeDetail).IsNull();
        await Assert.That(JsonDefaults.Serialize(actual.Get<PartitionMovePhaseResult>())
            .SequenceEqual(JsonDefaults.Serialize(Fence(owners)))).IsTrue();
    }
}
