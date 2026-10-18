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
                if (PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, capture.SourceOwner))
                {
                    EventVectorParentScopes.AddPartition(scopes, retained.Partition, scope.Resource,
                        scope.Kind, admission);
                }
            }
        }
    }
}
