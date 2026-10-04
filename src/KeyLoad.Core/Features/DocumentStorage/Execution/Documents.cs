using System.Text.Json;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static byte[] DocumentKey(PartitionRef partition, string collection, string id) => DocumentStorageKeys.RecordKey(partition, collection, id);
    /// <summary>Reads the persisted document visibility epoch at the current storage cut.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Document collection.</param>
    /// <returns>The persisted epoch, or zero before the first change.</returns>
    public long DocumentEpoch(IKeyValueView view, PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        return view.ReadOwnedValue(KeySpace.Partition("document-epoch", partition, collection)) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : 0;
    }
    private static void CheckRevision(long actual, long? expected)
    {
        if (expected is { } value && (value < 0 || value != actual))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, "The expected revision does not match.");
        }
    }
    private void UpdateIndexes(IAtomicTransaction tx, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord? before, DocumentRecord? after)
    {
        foreach (var index in resource.Indexes)
        {
            foreach (var field in index.Fields)
            {
                Authorization.RequireFieldUse(principal, resource, field);
            }

            if (before is not null && IndexValues(before.Json, index) is { } oldValues)
            {
                tx.Delete(IndexKey(before.Reference.Partition, resource.Name, index, oldValues, before.Reference.Id));
                if (index.Unique)
                {
                    tx.Delete(UniqueKey(before.Reference.Partition, resource.Name, index, oldValues));
                }
            }
            if (after is null || IndexValues(after.Json, index) is not { } values)
            {
                continue;
            }

            if (index.Unique)
            {
                var uniqueKey = UniqueKey(after.Reference.Partition, resource.Name, index, values);
                if (tx.ReadOwnedValue(uniqueKey) is { } occupant && NativeSerialization.Deserialize<string>(occupant) != after.Reference.Id)
                {
                    throw Errors.Fail(ErrorCode.Conflict, "A partition-scoped unique index value is already present.");
                }

                tx.PutRecord(uniqueKey, after.Reference.Id);
            }
            tx.PutRecord(IndexKey(after.Reference.Partition, resource.Name, index, values, after.Reference.Id), after.Reference.Id);
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
    /// <summary>Reads one document after row and field authorization.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="reference">Document identity.</param>
    /// <returns>The projected document, or null when unavailable.</returns>
    public DocumentResult? GetDocument(string principalId, EntityRef reference) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        var resource = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        var record = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        return record is null || record.Deleted || !Authorization.CanReadRow(principal, record.Access) ? null : Project(principal, resource, record);
    });
    /// <summary>Projects one already authorized document using persisted field policies.</summary>
    /// <param name="principal">Persisted principal.</param>
    /// <param name="resource">Persisted resource policy.</param>
    /// <param name="record">Document at the current read cut.</param>
    /// <returns>An owned projected document result.</returns>
    public DocumentResult Project(PrincipalRecord principal, ResourceDefinition resource, DocumentRecord record)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(record);
        var json = Authorization.Project(principal, resource.FieldPolicies, record.Json, out var omitted);
        return new(record.Reference, record.Revision, json, omitted.Length != 0, [.. omitted]);
    }
    /// <summary>Runs one authorized query callback inside a consistent storage read gate.</summary>
    /// <typeparam name="T">Owned callback result type.</typeparam>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Collection to query.</param>
    /// <param name="read">Gate-scoped read callback.</param>
    /// <returns>The callback's owned result.</returns>
    public T WithQueryView<T>(string principalId, PartitionRef partition, string collection,
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read) => Store.Read(view =>
    {
        ValidatePartition(partition);
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        return read(view, principal, resource);
    });
    /// <summary>Reads one visible document from the caller's existing storage cut.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted principal.</param>
    /// <param name="reference">Document identity.</param>
    /// <returns>The visible record, or null when unavailable.</returns>
    public DocumentRecord? ReadVisibleDocument(IKeyValueView view, PrincipalRecord principal, EntityRef reference)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(reference);
        var document = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        return document is { Deleted: false } && Authorization.CanReadRow(principal, document.Access) ? document : null;
    }
    /// <summary>Runs a callback over visible documents after resource authorization.</summary>
    /// <typeparam name="T">Owned callback result type.</typeparam>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Collection to read.</param>
    /// <param name="read">Callback over visible owned records.</param>
    /// <param name="capability">Required collection capability.</param>
    /// <param name="fieldUses">Optional fields requiring use authorization.</param>
    /// <returns>The callback's owned result.</returns>
    public T WithDocuments<T>(string principalId, PartitionRef partition, string collection, Func<PrincipalRecord, ResourceDefinition, DocumentRecord[], T> read,
        Capability capability = Capability.DocumentsRead, string[]? fieldUses = null)
        => Store.Read(view =>
        {
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, partition, collection, capability);
            var resource = Resource(view, partition, collection, ResourceKind.Collection);
            foreach (var path in fieldUses ?? [])
            {
                Authorization.RequireFieldUse(principal, resource, path);
            }

            return read(principal, resource, ReadVisibleDocuments(view, principal, partition, collection));
        });
    /// <summary>Materializes visible documents under the supplied read budget.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Collection to read.</param>
    /// <param name="budget">Shared read budget, or the configured default.</param>
    /// <returns>Owned visible document records.</returns>
    public DocumentRecord[] ReadVisibleDocuments(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection,
        ReadExecutionBudget? budget = null)
    {
        budget ??= new(Limits, timeProvider: Clock);
        var records = new List<DocumentRecord>();
        VisitVisibleDocuments(view, principal, partition, collection, budget, records.Add);
        return records.ToArray();
    }

    /// <summary>Visits visible owned documents inside the caller's existing read cut.</summary>
    /// <param name="view">The gate-scoped read view.</param>
    /// <param name="principal">Persisted, verified principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <param name="budget">Shared cancellation and cumulative resource limits.</param>
    /// <param name="visitor">Synchronous callback; it cannot escape or mutate the read view.</param>
    public void VisitVisibleDocuments(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        string collection, ReadExecutionBudget budget, Action<DocumentRecord> visitor)
        => VisibleDocumentReads.Visit(this, view, principal, partition, collection, budget, visitor);
}
