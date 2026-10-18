using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void CaptureEventVectorRoster(IKeyValueView bounded, PrincipalRecord principal,
        EventFeedControlRequest request, PhysicalShardRecord sourceOwner, PhysicalShardRecord defaultOwner,
        EventVectorRosterReadResult roster, ReadExecutionBudget work,
        long appliedCut, ImmutableArray<EventVectorEntry>.Builder entries,
        ImmutableArray<EventVectorCoverageRow>.Builder heads, CancellationToken cancellationToken)
    {
        foreach (var retained in roster.Entries)
        {
            work.Check();
            cancellationToken.ThrowIfCancellationRequested();
            var partition = retained.Partition;
            var placement = ResolveRegisteredPlacement(partition, defaultOwner);
            if (placement.PhysicalShardId != sourceOwner.PhysicalShardId
                || placement.Incarnation != sourceOwner.Incarnation
                || placement.PlacementEpoch != sourceOwner.PlacementEpoch
                || !placement.VoterIds.SequenceEqual(sourceOwner.VoterIds, StringComparer.Ordinal))
            {
                continue;
            }
            EventVectorCoveragePartition(bounded, principal, request, partition, sourceOwner,
                work, appliedCut, entries, heads, cancellationToken);
        }
    }
}
