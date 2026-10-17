using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsRetireJournalShape
{
    private const int InitialFamily = 0;
    private const int FirstBatch = 1;
    internal static bool Valid(PartitionMovePhaseResult phase, PartitionRef partition)
    {
        if (phase.Stage != PartitionMovePeerStage.Retire || phase.MoveId == Guid.Empty
            || phase.Control is not null || phase.Fence is not null || phase.InstalledReceipt is not null
            || phase.PublishedPlacement is not null || phase.Grant is not null || phase.ControlledCommand is not null
            || phase.ControlledEffect is not null || phase.ReceiverIssuance is not null || phase.RetireCancellation is not null
            || phase.Cleanup is not { } cleanup || cleanup.Version != PartitionMoveProtocol.Version
            || cleanup.MoveId != phase.MoveId || cleanup.Partition != partition
            || cleanup.Stage != PartitionMovePeerStage.Retire || cleanup.Role != PartitionMoveCleanupRole.Source
            || cleanup.ControlIntentDigest != phase.Journal.ControlIntentDigest
            || cleanup.NextFamily < InitialFamily || cleanup.NextFamily > PartitionMoveCleanupFamilies.All.Length
            || cleanup.NextBatch < FirstBatch || phase.Journal.EffectDigest is not null)
        {
            return false;
        }
        return cleanup.Completion is null || cleanup.NextFamily == PartitionMoveCleanupFamilies.All.Length
            && SameJournal(cleanup.Completion, phase.Journal);
    }

    private static bool SameJournal(PartitionMoveJournalReceipt actual, PartitionMoveJournalReceipt expected)
        => actual.CommandId == expected.CommandId && actual.AppliedPosition == expected.AppliedPosition
            && actual.ControlIntentDigest == expected.ControlIntentDigest && actual.EffectDigest == expected.EffectDigest
            && actual.PhysicalOwner.PhysicalShardId == expected.PhysicalOwner.PhysicalShardId
            && actual.PhysicalOwner.Incarnation == expected.PhysicalOwner.Incarnation
            && actual.PhysicalOwner.PlacementEpoch == expected.PhysicalOwner.PlacementEpoch
            && actual.PhysicalOwner.VoterIds.SequenceEqual(expected.PhysicalOwner.VoterIds, StringComparer.Ordinal);
}
