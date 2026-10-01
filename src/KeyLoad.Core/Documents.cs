using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static byte[] DocumentKey(PartitionRef partition, string collection, string id) => KeySpace.Partition("document", partition, collection, id);
    private MutationReceipt Put(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutDocument put, DateTimeOffset now)
    {
        JsonData.Identifier(put.Id);
        var resource = Resource(tx, partition, put.Collection, ResourceKind.Collection);
        if (resource.Authority != DocumentAuthority.Document) throw Errors.Fail(ErrorCode.PermissionDenied, "Event-authoritative documents cannot be mutated directly.");
        var key = DocumentKey(partition, put.Collection, put.Id);
        var previous = tx.GetRecord<DocumentRecord>(key);
        CheckRevision(previous?.Revision ?? 0, put.ExpectedRevision);
        if (previous is { Deleted: false })
        {
            Authorization.RequireWriteRow(principal, previous.Access);
            Authorization.RequireReplacement(principal, resource, put.ExplicitReplacement);
        }
        var json = JsonData.Validate(put.Json, Limits);
        foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
        var access = put.Access ?? previous?.Access ?? new RowAccess();
        Authorization.RequireWriteRow(principal, access);
        var document = new DocumentRecord(new(partition, put.Collection, put.Id), checked((previous?.Revision ?? 0) + 1), json, access, now);
        UpdateIndexes(tx, principal, resource, previous is { Deleted: false } ? previous : null, document);
        tx.PutRecord(key, document);
        return new("putDocument", put.Collection, put.Id, document.Revision);
    }
    private MutationReceipt Patch(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PatchDocument patch, DateTimeOffset now)
    {
        if (patch.Patches.Length is < 1 or > 256) throw Errors.Fail(ErrorCode.ResourceExhausted, "The patch exceeds its operation budget.");
        var resource = Resource(tx, partition, patch.Collection, ResourceKind.Collection);
        if (resource.Authority != DocumentAuthority.Document) throw Errors.Fail(ErrorCode.PermissionDenied, "Event-authoritative documents cannot be mutated directly.");
        var key = DocumentKey(partition, patch.Collection, patch.Id);
        var previous = tx.GetRecord<DocumentRecord>(key);
        if (previous is null || previous.Deleted) throw Errors.Fail(ErrorCode.NotFound, "The document is unavailable.");
        CheckRevision(previous.Revision, patch.ExpectedRevision);
        Authorization.RequireWriteRow(principal, previous.Access);
        foreach (var field in patch.Patches) Authorization.RequireFieldWrite(principal, resource, field.Path);
        var updated = previous with { Json = JsonData.Patch(previous.Json, patch.Patches, Limits), Revision = checked(previous.Revision + 1), UpdatedAt = now };
        UpdateIndexes(tx, principal, resource, previous, updated);
        tx.PutRecord(key, updated);
        return new("patchDocument", patch.Collection, patch.Id, updated.Revision);
    }
    private MutationReceipt Delete(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, DeleteDocument delete, DateTimeOffset now)
    {
        var resource = Resource(tx, partition, delete.Collection, ResourceKind.Collection);
        if (resource.Authority != DocumentAuthority.Document) throw Errors.Fail(ErrorCode.PermissionDenied, "Event-authoritative documents cannot be mutated directly.");
        var key = DocumentKey(partition, delete.Collection, delete.Id);
        var previous = tx.GetRecord<DocumentRecord>(key);
        if (previous is null || previous.Deleted) throw Errors.Fail(ErrorCode.NotFound, "The document is unavailable.");
        CheckRevision(previous.Revision, delete.ExpectedRevision);
        Authorization.RequireWriteRow(principal, previous.Access);
        UpdateIndexes(tx, principal, resource, previous, null);
        tx.PutRecord(key, previous with { Json = "{}", Deleted = true, Revision = checked(previous.Revision + 1), UpdatedAt = now });
        return new("deleteDocument", delete.Collection, delete.Id, previous.Revision + 1);
    }
    private static void CheckRevision(long actual, long? expected)
    {
        if (expected is { } value && (value < 0 || value != actual))
            throw Errors.Fail(ErrorCode.RevisionConflict, "The expected revision does not match.");
    }
    private void UpdateIndexes(IAtomicTransaction tx, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord? before, DocumentRecord? after)
    {
        foreach (var index in resource.Indexes)
        {
            foreach (var field in index.Fields) Authorization.RequireFieldUse(principal, resource, field);
            if (before is not null && IndexValues(before.Json, index) is { } oldValues)
            {
                tx.Delete(IndexKey(before.Reference.Partition, resource.Name, index, oldValues, before.Reference.Id));
                if (index.Unique) tx.Delete(UniqueKey(before.Reference.Partition, resource.Name, index, oldValues));
            }
            if (after is not null && IndexValues(after.Json, index) is { } values)
            {
                if (index.Unique)
                {
                    var uniqueKey = UniqueKey(after.Reference.Partition, resource.Name, index, values);
                    if (tx.Get(uniqueKey) is { } occupant && JsonDefaults.Deserialize<string>(occupant) != after.Reference.Id)
                        throw Errors.Fail(ErrorCode.Conflict, "A partition-scoped unique index value is already present.");
                    tx.PutRecord(uniqueKey, after.Reference.Id);
                }
                tx.PutRecord(IndexKey(after.Reference.Partition, resource.Name, index, values, after.Reference.Id), after.Reference.Id);
            }
        }
    }
    private static object?[]? IndexValues(string json, IndexDefinition index)
    {
        using var document = JsonDocument.Parse(json);
        var values = index.Fields.Select(f => JsonData.Scalar(document.RootElement, f)).ToArray();
        return values.Any(v => v is null && !index.IncludeNull || v is MissingValue && !index.IncludeMissing) ? null : values;
    }
    private static byte[] IndexKey(PartitionRef partition, string collection, IndexDefinition index, object?[] values, string id)
        => KeySpace.Partition("index", partition, new object?[] { collection, index.Name }.Concat(values).Append(id).ToArray());
    private static byte[] UniqueKey(PartitionRef partition, string collection, IndexDefinition index, object?[] values)
        => KeySpace.Partition("unique", partition, new object?[] { collection, index.Name }.Concat(values).ToArray());
    public DocumentResult? GetDocument(string principalId, EntityRef reference) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        var resource = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        var record = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        return record is null || record.Deleted || !Authorization.CanReadRow(principal, record.Access) ? null : Project(principal, resource, record);
    });
    public DocumentResult Project(PrincipalRecord principal, ResourceDefinition resource, DocumentRecord record)
    {
        var json = Authorization.Project(principal, resource.FieldPolicies, record.Json, out var omitted);
        return new(record.Reference, record.Revision, json, omitted.Length != 0, omitted);
    }
    public T WithQueryView<T>(string principalId, PartitionRef partition, string collection,
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read) => Store.Read(view =>
    {
        ValidatePartition(partition);
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        return read(view, principal, resource);
    });
    public DocumentRecord? ReadVisibleDocument(IKeyValueView view, PrincipalRecord principal, EntityRef reference)
    {
        var document = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        return document is { Deleted: false } && Authorization.CanReadRow(principal, document.Access) ? document : null;
    }
    public T WithDocuments<T>(string principalId, PartitionRef partition, string collection, Func<PrincipalRecord, ResourceDefinition, DocumentRecord[], T> read,
        Capability capability = Capability.DocumentsRead, string[]? fieldUses = null)
        => Store.Read(view =>
        {
            var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
            Authorization.Require(principal, partition, collection, capability);
            var resource = Resource(view, partition, collection, ResourceKind.Collection);
            foreach (var path in fieldUses ?? []) Authorization.RequireFieldUse(principal, resource, path);
            return read(principal, resource, ReadVisibleDocuments(view, principal, partition, collection));
        });
    public DocumentRecord[] ReadVisibleDocuments(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection)
    {
        var page = view.Scan(KeySpace.Partition("document", partition, collection), Limits.MaxScanRecords);
        if (page.HasMore) throw Errors.Fail(ErrorCode.BudgetExceeded, "The collection scan exceeds its budget. Use an index or a narrower partition.");
        return page.Records.Select(kv => JsonDefaults.Deserialize<DocumentRecord>(kv.Value))
            .Where(d => !d.Deleted && Authorization.CanReadRow(principal, d.Access)).ToArray();
    }
}
