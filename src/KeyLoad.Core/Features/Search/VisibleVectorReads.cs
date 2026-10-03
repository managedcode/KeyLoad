using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

/// <summary>Visits visible, revision-matching vector/document pairs within one cut.</summary>
internal static class VisibleVectorReads
{
    private const string VectorSpace = "vector";
    private const string VectorScanExceeded = "The exact vector scan exceeds its budget.";

    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorRecord> visitor)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(visitor);
        budget.Check();
        var range = budget.VisitRange(view, KeySpace.Partition(VectorSpace, partition, collection, field),
            database.Limits.MaxScanRecords, (key, value) =>
            {
                var vector = NativeSerialization.Deserialize<VectorRecord>(value);
                var document = budget.ReadRecord<DocumentRecord>(view,
                    DocumentStorageKeys.RecordKey(partition, collection, vector.DocumentId));
                if (document is { Deleted: false } && document.Revision == vector.DocumentRevision
                    && database.Authorization.CanReadRow(principal, document.Access))
                {
                    budget.Check();
                    visitor(document, vector);
                }
                return true;
            });
        if (range.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, VectorScanExceeded);
        }
    }
}
