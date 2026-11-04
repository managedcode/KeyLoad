using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Search;

/// <summary>Visits visible, revision-matching vector/document pairs within one cut.</summary>
internal static class VisibleVectorReads
{
    internal const string VectorKeySpace = "vector";
    private const string InvalidVectorIdentity = "The stored vector identity does not match its canonical key.";

    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorRecord> visitor, AnnReadLifetimeMetadata? sourceMetadata = null, AnnSeedWork? scopeWork = null)
    {
        ArgumentNullException.ThrowIfNull(visitor) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        VisitPacked(database, view, principal, partition, collection, field, budget,
            (document, vector, owned) => visitor(document, owned ?? vector.Materialize()), sourceMetadata, scopeWork);
    }

    internal static void VisitPacked(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorStorageRecord, VectorRecord?> visitor, AnnReadLifetimeMetadata? sourceMetadata = null,
        AnnSeedWork? scopeWork = null)
    {
        var read = CreateVisitor(database, view, principal, partition, collection, field, budget, visitor, sourceMetadata, scopeWork);
        _ = budget.VisitAll(view, KeySpace.Partition(VectorKeySpace, partition, collection, field), read);
    }

    internal static async ValueTask<StorageScanResult> VisitPackedAsync(DatabaseEngine database, ICooperativeRangeReadView view,
        PrincipalRecord principal, PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorStorageRecord, VectorRecord?> visitor)
    {
        var prefix = KeySpace.Partition(VectorKeySpace, partition, collection, field);
        using var documents = view.OpenOrderedPointReader(DocumentStorageKeys.Prefix(partition, collection), budget.Cancellation);
        using var lineages = view.OpenOrderedPointReader(VectorProjectionKeys.LineagePrefix(partition, collection, field), budget.Cancellation);
        var read = CreateVisitor(database, view, principal, partition, collection, field, budget, visitor, null, null,
            new(prefix, documents, lineages));
        return await ReadExecutionBudgetStorageReads.VisitAllAsync(budget, view, prefix, read);
    }

    private static StorageRecordVisitor CreateVisitor(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget budget,
        Action<DocumentRecord, VectorStorageRecord, VectorRecord?> visitor, AnnReadLifetimeMetadata? sourceMetadata, AnnSeedWork? scopeWork,
        OrderedVectorReads? ordered = null)
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
        var documentPrefix = ordered.HasValue ? Array.Empty<byte>() : DocumentStorageKeys.Prefix(partition, collection);
        var lineageScope = (Field: field,
            Prefix: ordered.HasValue ? Array.Empty<byte>() : VectorProjectionKeys.LineagePrefix(partition, collection, field));
        var documents = new ReadExecutionRecordReader<DocumentRecord>(budget, view);
        var lineages = new ReadExecutionRecordReader<VectorProjectionLineage>(budget, view);
        return (key, value) =>
        {
            var vector = OrleansSerialization.Deserialize<VectorStorageRecord>(value);
            var suffix = default(ReadOnlySpan<byte>);
            DocumentRecord? document;
            if (ordered is { } cursors)
            {
                suffix = RequireCanonicalSuffix(key, cursors.VectorPrefix, field, vector);
                document = documents.Read(cursors.Documents, suffix);
            }
            else
            {
                document = documents.Read(DocumentStorageKeys.RecordKey(documentPrefix, vector.DocumentId));
            }
            if (document is not { Deleted: false } || document.Revision != vector.DocumentRevision
                || !database.Authorization.CanReadRow(principal, document.Access)
                || (sourceMetadata is not null && !database.Authorization.CanReadRow(sourceMetadata.Principal, document.Access)))
            {
                return true;
            }
            var lineage = VectorProjectionEligibility.ReadLineage(partition, collection, vector, lineages, lineageScope,
                ordered?.Lineages, suffix);
            var owned = lineage is null ? null : vector.Materialize();
            if (lineage is null || VectorProjectionEligibility.IsEligible(database, view, principal, partition, collection,
                    document, owned!, lineage, budget, resolver)
                && (sourceMetadata is null || VectorProjectionEligibility.IsEligible(database, view, sourceMetadata.Principal,
                    partition, collection, document, owned!, lineage, budget, resolver, sourcePolicies: true)))
            {
                budget.Check();
                visitor(document, vector, owned);
            }
            return true;
        };
    }

    private static ReadOnlySpan<byte> RequireCanonicalSuffix(ReadOnlySpan<byte> key, byte[] prefix, string field,
        VectorStorageRecord vector)
    {
        if (!key.StartsWith(prefix) || vector.DocumentId is not { } id || !string.Equals(vector.Field, field, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidVectorIdentity);
        }
        var suffix = key[prefix.Length..];
        try
        {
            if (KeyCodec.MatchesComponents(suffix, id))
            {
                return suffix;
            }
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidVectorIdentity);
        }
        throw Errors.Fail(ErrorCode.Corruption, InvalidVectorIdentity);
    }

    private readonly record struct OrderedVectorReads(byte[] VectorPrefix, IOrderedPointReader Documents, IOrderedPointReader Lineages);
}
