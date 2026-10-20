using System.Text.Json;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageExecutionDocuments operations against the caller's database context.</summary>
public static class DocumentStorageExecutionDocuments
{
    internal const string UniqueIndexSpace = "unique";
    internal static byte[] DocumentKey(PartitionRef partition, string collection, string id) => DocumentStorageKeys.RecordKey(partition, collection, id);
    /// <summary>Reads the persisted document visibility epoch at the current storage cut.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Document collection.</param>
    /// <returns>The persisted epoch, or zero before the first change.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static long DocumentEpoch(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(engine);
        const string DocumentEpochSpaceText = "document-epoch";
        const int InitialDocumentEpoch = 0;
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        return view.ReadOwnedValue(KeySpace.Partition(DocumentEpochSpaceText, partition, collection)) is { } bytes ? OrleansSerialization.Deserialize<long>(bytes) : InitialDocumentEpoch;
    }

    internal static void CheckRevision(long actual, long? expected)
    {
        const int ValueValidationBoundary = 0;
        const string CheckRevisionDetailText = "The expected revision does not match.";
        if (expected is { } value && (value < ValueValidationBoundary || value != actual))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, CheckRevisionDetailText);
        }
    }

    internal static void UpdateIndexes(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord? before, DocumentRecord? after)
    {
        const string UpdateIndexesDetailText = "A partition-scoped unique index value is already present.";
        foreach (var index in resource.Indexes)
        {
            foreach (var field in index.Fields)
            {
                engine.Authorization.RequireFieldUse(principal, resource, field);
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
                if (tx.ReadOwnedValue(uniqueKey) is { } occupant && OrleansSerialization.Deserialize<string>(occupant) != after.Reference.Id)
                {
                    throw Errors.Fail(ErrorCode.Conflict, UpdateIndexesDetailText);
                }

                tx.PutRecord(uniqueKey, after.Reference.Id);
            }

            tx.PutRecord(IndexKey(after.Reference.Partition, resource.Name, index, values, after.Reference.Id), after.Reference.Id);
        }
    }

    internal static object?[]? IndexValues(string json, IndexDefinition index)
    {
        using var document = JsonDocument.Parse(json);
        var values = index.Fields.Select(f => JsonData.Scalar(document.RootElement, f)).ToArray();
        return values.Any(v => v is null && !index.IncludeNull || v is MissingValue && !index.IncludeMissing) ? null : values;
    }

    internal static byte[] IndexKey(PartitionRef partition, string collection, IndexDefinition index, object?[] values, string id) => KeySpace.Partition(nameof(index), partition, new object?[] { collection, index.Name }.Concat(values).Append(id).ToArray());
    internal static byte[] UniqueKey(PartitionRef partition, string collection, IndexDefinition index, object?[] values) => KeySpace.Partition(UniqueIndexSpace, partition, new object?[] { collection, index.Name }.Concat(values).ToArray());
    /// <summary>Reads one document after row and field authorization.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "reference">Document identity.</param>
    /// <param name = "minimumToken">Optional acknowledged minimum in the same physical placement.</param>
    /// <param name = "cancellationToken">Cancellation checked within the final authorized read cut.</param>
    /// <returns>The projected document, or null when unavailable.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static DocumentResult? GetDocument(this DatabaseEngine engine, string principalId, EntityRef reference, CommitToken? minimumToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            return engine.ReadDocumentAtCut(view, principal, reference, minimumToken, cancellationToken);
        });
    }

    /// <summary>Projects one already authorized document using persisted field policies.</summary>
    /// <param name = "principal">Persisted principal.</param>
    /// <param name = "resource">Persisted resource policy.</param>
    /// <param name = "record">Document at the current read cut.</param>
    /// <returns>An owned projected document result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static DocumentResult Project(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord record)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ProjectWithLineage(principal, resource, record).Value;
    }

    internal static DocumentProjection ProjectWithLineage(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord record)
    {
        const int RedactedEmptyCount = 0;
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(record);
        var json = engine.Authorization.Project(principal, resource.FieldPolicies, record.Json, out var omitted);
        var visible = FieldRedactionMetadata.Visible(engine.Authorization, principal, resource, omitted);
        var value = new DocumentResult(record.Reference, record.Revision, json, omitted.Length != RedactedEmptyCount, visible);
        return new(value, [.. omitted]);
    }

    /// <summary>Runs one authorized query callback inside a consistent storage read gate.</summary>
    /// <typeparam name = "T">Owned callback result type.</typeparam>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Collection to query.</param>
    /// <param name = "read">Gate-scoped read callback.</param>
    /// <returns>The callback's owned result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static T WithQueryView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.WithQueryView(principalId, partition, collection, Capability.None, read);
    }

    /// <summary>Checks additional execution query capabilities before reading private resource metadata in the same cut.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "principalId">The principal Id.</param>
    /// <param name = "partition">The partition.</param>
    /// <param name = "collection">The collection.</param>
    /// <param name = "additionalRequiredCapabilities">The additional Required Capabilities.</param>
    /// <param name = "read">The read.</param>
    /// <param name = "budget">The budget.</param>
    /// <param name = "beforeResourceAdmission">The before Resource Admission.</param>
    internal static T WithQueryView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, Capability additionalRequiredCapabilities, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read, ReadExecutionBudget? budget = null, Action<IKeyValueView>? beforeResourceAdmission = null) => engine.Store.Read(raw =>
    {
        EngineResourceAccess.ValidatePartition(partition);
        var view = budget?.CreateView(raw) ?? raw;
        var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
        engine.Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        if (additionalRequiredCapabilities != Capability.None)
        {
            engine.Authorization.Require(principal, partition, collection, additionalRequiredCapabilities);
        }

        beforeResourceAdmission?.Invoke(view);
        var resource = ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, collection, ResourceKind.Collection);
        return read(raw, principal, resource);
    });
    /// <summary>Reads one visible document from the caller's existing storage cut.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "principal">Persisted principal.</param>
    /// <param name = "reference">Document identity.</param>
    /// <returns>The visible record, or null when unavailable.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static DocumentRecord? ReadVisibleDocument(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EntityRef reference)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(reference);
        var resource = engine.Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        reference = EntityReferenceLifetime.Bind(view, reference, resource);
        var document = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (document is not null)
        {
            EntityReferenceLifetime.RequireStored(view, document.Reference, resource);
        }

        return document is { Deleted: false } && engine.Authorization.CanReadRow(principal, document.Access) ? document : null;
    }

    /// <summary>Runs a callback over visible documents after resource authorization.</summary>
    /// <typeparam name = "T">Owned callback result type.</typeparam>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Collection to read.</param>
    /// <param name = "read">Callback over visible owned records.</param>
    /// <param name = "capability">Required collection capability.</param>
    /// <param name = "fieldUses">Optional fields requiring use authorization.</param>
    /// <returns>The callback's owned result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static T WithDocuments<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, Func<PrincipalRecord, ResourceDefinition, DocumentRecord[], T> read, Capability capability = Capability.DocumentsRead, string[]? fieldUses = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view =>
        {
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            engine.Authorization.Require(principal, partition, collection, capability);
            var resource = engine.Resource(view, partition, collection, ResourceKind.Collection);
            foreach (var path in fieldUses ?? [])
            {
                engine.Authorization.RequireFieldUse(principal, resource, path);
            }

            return read(principal, resource, engine.ReadVisibleDocuments(view, principal, partition, collection));
        });
    }

    /// <summary>Materializes visible documents under the supplied read budget.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "principal">Persisted principal.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Collection to read.</param>
    /// <param name = "budget">Shared read budget, or the configured default.</param>
    /// <returns>Owned visible document records.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static DocumentRecord[] ReadVisibleDocuments(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection, ReadExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        budget ??= new(engine.OperationLimitsOptions, timeProvider: engine.Clock);
        var records = new List<DocumentRecord>();
        engine.VisitVisibleDocuments(view, principal, partition, collection, budget, records.Add);
        return records.ToArray();
    }

    /// <summary>Visits visible owned documents inside the caller's existing read cut.</summary>
    /// <param name = "view">The gate-scoped read view.</param>
    /// <param name = "principal">Persisted, verified principal.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Configured collection.</param>
    /// <param name = "budget">Shared cancellation and cumulative resource limits.</param>
    /// <param name = "visitor">Synchronous callback; it cannot escape or mutate the read view.</param>

    /// <param name = "engine">The shared database execution context.</param>
    public static void VisitVisibleDocuments(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection, ReadExecutionBudget budget, Action<DocumentRecord> visitor)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(partition);
        VisibleDocumentReads.Visit(engine, view, principal, partition, collection, budget, visitor);
    }
}
