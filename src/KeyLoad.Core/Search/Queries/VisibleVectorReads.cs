using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Search;

/// <summary>Visits visible, revision-matching vector/document pairs within one cut.</summary>
internal static class VisibleVectorReads
{
    internal const string VectorKeySpace = "vector";

    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorRecord> visitor, AnnReadLifetimeMetadata? sourceMetadata = null, AnnSeedWork? scopeWork = null)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(principal) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(collection) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(field) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(visitor) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.Check();
        var resolver = sourceMetadata is null ? null : new AnnLifetimeResourceResolver(sourceMetadata,
            scopeWork ?? throw new ArgumentNullException(nameof(scopeWork)), principal);
        _ = budget.VisitAll(view, KeySpace.Partition(VectorKeySpace, partition, collection, field), (key, value) =>
            {
                var vector = OrleansSerialization.Deserialize<VectorRecord>(value);
                var document = budget.ReadRecord<DocumentRecord>(view,
                    DocumentStorageKeys.RecordKey(partition, collection, vector.DocumentId));
                if (document is { Deleted: false } && document.Revision == vector.DocumentRevision
                    && database.Authorization.CanReadRow(principal, document.Access)
                    && (sourceMetadata is null || database.Authorization.CanReadRow(sourceMetadata.Principal, document.Access))
                    && VectorProjectionEligibility.IsEligible(database, view, principal, partition, collection,
                        document, vector, budget, resolver)
                    && (sourceMetadata is null || VectorProjectionEligibility.IsEligible(database, view, sourceMetadata.Principal,
                        partition, collection, document, vector, budget, resolver, sourcePolicies: true)))
                {
                    budget.Check();
                    visitor(document, vector);
                }
                return true;
            });
    }
}
