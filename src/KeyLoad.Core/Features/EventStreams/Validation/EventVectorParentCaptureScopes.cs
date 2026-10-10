using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorParentCaptureScopes
{
    internal static void Add(SortedDictionary<string, EventVectorAuthorizationScope> scopes,
        EventVectorCoverageSemanticIdentity coverage, EventFeedScope scope, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEntryCount(coverage.OriginalCaptureBytes.Length);
        foreach (var raw in coverage.OriginalCaptureBytes)
        {
            admission.RequireEncodedBytes(raw.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(raw.Span);
            var catalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(capture.PhysicalCatalog.Value.Span);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            admission.RequireEntryCount(capture.AtomicPartitionRosterRows.Length);
            foreach (var row in capture.AtomicPartitionRosterRows)
            {
                admission.RequireEncodedBytes(checked(row.Key.Length + row.Value.Length));
                var retained = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(row.Value.Span);
                if (Belongs(capture, catalog.DefaultShard, retained.Partition, admission))
                {
                    EventVectorParentScopes.AddPartition(scopes, retained.Partition, scope.Resource,
                        scope.Kind, admission);
                }
            }
        }
    }

    private static bool Belongs(EventVectorCoverageCapture capture, PhysicalShardRecord defaultOwner,
        PartitionRef partition, EventVectorAdmissionPolicy admission)
    {
        var key = AtomicPartitionPlacementSerialization.RowKey(partition);
        foreach (var row in capture.PlacementRows)
        {
            if (!row.Key.Span.SequenceEqual(key))
            { continue; }
            admission.RequireEncodedBytes(checked(row.Key.Length + row.Value.Length));
            var placement = NativeSerialization.Deserialize<AtomicPartitionPlacementV1>(row.Value.Span);
            AtomicPartitionPlacementValidation.ValidateRowShape(placement, partition);
            return placement.PhysicalShardId == capture.SourceOwner.PhysicalShardId
                && placement.Incarnation == capture.SourceOwner.Incarnation
                && placement.PlacementEpoch == capture.SourceOwner.PlacementEpoch
                && placement.VoterIds.SequenceEqual(capture.SourceOwner.VoterIds, StringComparer.Ordinal);
        }
        return PhysicalOwnerEntryValidation.SameOwner(defaultOwner, capture.SourceOwner);
    }
}
