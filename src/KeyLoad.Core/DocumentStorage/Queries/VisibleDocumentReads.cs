using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.DocumentStorage;

/// <summary>Decodes visible documents without retaining a raw scan page.</summary>
internal static class VisibleDocumentReads
{
    private const string CollectionMissing = "The collection resource is unavailable.";
    private const string CollectionScanExceeded = "The collection scan exceeds its budget. Use an index or a narrower partition.";

    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, ReadExecutionBudget budget, Action<DocumentRecord> visitor)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(visitor);
        budget.Check();
        var metadata = budget.CreateView(view);
        var definition = DatabaseEngine.BindResourceDefinition(metadata, partition, collection, ResourceKind.Collection);
        var lifetime = ResourceLifetimeCatalog.Read(metadata, partition.TenantId, partition.DatabaseId, collection, definition)
            ?? throw Errors.Fail(ErrorCode.NotFound, CollectionMissing);
        var range = budget.VisitRange(view, DocumentStorageKeys.Prefix(partition, collection),
            database.Limits.MaxScanRecords, (key, value) =>
            {
                var document = NativeSerialization.Deserialize<DocumentRecord>(value);
                EntityReferenceLifetime.RequireStored(document.Reference, lifetime);
                if (!document.Deleted && database.Authorization.CanReadRow(principal, document.Access))
                {
                    budget.Check();
                    visitor(document);
                }
                return true;
            });
        if (range.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, CollectionScanExceeded);
        }
    }
}
