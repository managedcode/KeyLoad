using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
namespace KeyLoad.Core.Features.Search;

/// <summary>Captures the exact original-cut dependency closure of existing document/vector readers.</summary>
internal static class NativeSearchLookupClosure
{
    internal static void Capture(DatabaseEngine database, IScopedReadCapture source, PrincipalRecord principal,
        PartitionRef partition, string collection, string? vectorField, ReadExecutionBudget budget)
    {
        source.CapturePrefix(DocumentStorageKeys.Prefix(partition, collection));
        source.CaptureExact(KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection));
        if (vectorField is null)
        { return; }
        var prefix = KeySpace.Partition(VisibleVectorReads.VectorKeySpace, partition, collection, vectorField);
        source.CapturePrefix(prefix);
        source.VisitCapturedPrefix(prefix, (_, bytes) =>
        {
            budget.Check();
            source.AdmitRetainedBytes(bytes.Length);
            var vector = NativeSerialization.Deserialize<VectorRecord>(bytes);
            CaptureVector(database, source, principal, partition, collection, vector, budget);
            return true;
        });
    }

    private static void CaptureVector(DatabaseEngine database, IScopedReadCapture source, PrincipalRecord principal,
        PartitionRef partition, string collection, VectorRecord vector, ReadExecutionBudget budget)
    {
        budget.Check();
        var document = Read<DocumentRecord>(source, DocumentStorageKeys.RecordKey(partition, collection, vector.DocumentId));
        if (document is not { Deleted: false } || document.Revision != vector.DocumentRevision
            || !database.Authorization.CanReadRow(principal, document.Access))
        { return; }
        var lineage = Read<VectorProjectionLineage>(source,
            VectorProjectionKeys.Lineage(partition, collection, vector.Field, vector.DocumentId));
        if (lineage is null)
        { return; }
        VectorProjectionEligibility.ValidateLineage(lineage, partition, document, vector);
        source.CaptureExact(VectorProjectionKeys.Effect(partition, lineage));
        source.CaptureExact(KeySpace.Resource(partition.TenantId, partition.DatabaseId, lineage.SourceDocument.Collection));
        source.CaptureExact(DocumentStorageKeys.RecordKey(lineage.SourceDocument));
        budget.Check();
    }

    private static T? Read<T>(IScopedReadCapture source, byte[] key) where T : class
    {
        T? result = null;
        source.ReadCapturedValue(key, bytes =>
        {
            source.AdmitRetainedBytes(bytes.Length);
            result = NativeSerialization.Deserialize<T>(bytes);
        });
        return result;
    }
}
