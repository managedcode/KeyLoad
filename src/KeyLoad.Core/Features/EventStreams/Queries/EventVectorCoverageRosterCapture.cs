using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void CaptureEventVectorRoster(IKeyValueView bounded, PrincipalRecord principal,
        EventFeedControlRequest request, PhysicalShardRecord sourceOwner, PhysicalShardRecord defaultOwner,
        EventVectorRosterReadResult roster, ReadExecutionBudget work, EventVectorInventoryReadBudget inventory,
        long appliedCut, ImmutableArray<EventVectorEntry>.Builder entries,
        ImmutableArray<EventVectorCoverageRow>.Builder placements,
        ImmutableArray<EventVectorCoverageRow>.Builder heads, CancellationToken cancellationToken)
    {
        foreach (var retained in roster.Entries)
        {
            work.Check();
            cancellationToken.ThrowIfCancellationRequested();
            var partition = retained.Partition;
            var placement = ResolveRegisteredPlacement(bounded, partition, defaultOwner);
            var row = EventVectorCoverageRows.Optional(bounded,
                AtomicPartitionPlacementSerialization.RowKey(partition), inventory);
            if (row is not null)
            { placements.Add(row); }
            if (placement.PhysicalShardId != sourceOwner.PhysicalShardId
                || placement.Incarnation != sourceOwner.Incarnation
                || placement.PlacementEpoch != sourceOwner.PlacementEpoch
                || !placement.VoterIds.SequenceEqual(sourceOwner.VoterIds, StringComparer.Ordinal))
            { continue; }
            EventVectorCoveragePartition(bounded, principal, request, partition, sourceOwner,
                work, appliedCut, entries, heads, cancellationToken);
        }
    }
}
