using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class ScalarIndexCrashOperations
{
    private const long EmptyRevision = 0;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const int PositionStep = 1;
    private const int EmptyEvidenceBytes = 0;
    private const string ConflictOutcomeMessage = "The unique-index conflict batch did not return its canonical conflict outcome.";
    private const string ConflictMutationMessage = "The rejected unique-index batch changed canonical document state.";
    private const string EvidenceBoundMessage = "The scalar-index process evidence exceeded its existing fixed bound.";
    private const string IndexPathMessage = "Scalar-index recovery observation did not use the declared native index.";
    private static readonly string[] Values = [ScalarIndexCrashContract.ValueAlpha, ScalarIndexCrashContract.ValueBeta,
        ScalarIndexCrashContract.ValueGamma, ScalarIndexCrashContract.ValueDelta, ScalarIndexCrashContract.ValueEpsilon,
        ScalarIndexCrashContract.ValueRollback, ScalarIndexCrashContract.ValueZeta,
        ScalarIndexCrashContract.ValueTheta, ScalarIndexCrashContract.ValueHealthy];

    internal static void Configure(DatabaseEngine database)
    {
        var definition = new ResourceDefinition(ScalarIndexCrashContract.Collection, ResourceKind.Collection,
            ScalarIndexCrashContract.Partition.TransactionDomainId)
        { Indexes = [new(ScalarIndexCrashContract.IndexName, [ScalarIndexCrashContract.LabelPath], Unique: true)] };
        var request = new ConfigureResourceRequest(ScalarIndexCrashContract.Partition.TenantId,
            ScalarIndexCrashContract.Partition.DatabaseId, definition);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, request,
            Guid.Parse(ScalarIndexCrashContract.CollectionCommandText)).Get<ResourceDefinition>();
    }

    internal static void Seed(DatabaseEngine database)
        => Submit(database, ScalarIndexCrashContract.SeedCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.FirstId,
                ScalarIndexCrashContract.FirstInitialJson, EmptyRevision),
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.SecondId,
                ScalarIndexCrashContract.SecondInitialJson, EmptyRevision),
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.DeletedId,
                ScalarIndexCrashContract.DeletedInitialJson, EmptyRevision));

    internal static void ApplyAcknowledgedMutations(DatabaseEngine database)
    {
        _ = Submit(database, ScalarIndexCrashContract.ReplaceCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.FirstId,
                ScalarIndexCrashContract.ReplacedJson, FirstRevision, ExplicitReplacement: true));
        _ = Submit(database, ScalarIndexCrashContract.PatchCommandText,
            new PatchDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.SecondId,
                [new(ScalarIndexCrashContract.LabelPath, PatchKind.Set, JsonSerializer.Serialize(ScalarIndexCrashContract.ValueEpsilon))], FirstRevision));
        _ = Submit(database, ScalarIndexCrashContract.DeleteCommandText,
            new DeleteDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.DeletedId, FirstRevision));
    }

    internal static void RequireUniqueConflictHasNoEffects(DatabaseEngine database)
    {
        var result = Submit(database, ScalarIndexCrashContract.ConflictCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.ConflictId,
                ScalarIndexCrashContract.ConflictJson, EmptyRevision),
            new PatchDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.SecondId,
                [new(ScalarIndexCrashContract.LabelPath, PatchKind.Set, JsonSerializer.Serialize(ScalarIndexCrashContract.ValueDelta))], SecondRevision));
        if (result.Error != ErrorCode.Conflict)
        {
            throw new InvalidOperationException(ConflictOutcomeMessage);
        }
        var inserted = Read(database, ScalarIndexCrashContract.Partition, ScalarIndexCrashContract.ConflictId);
        var second = Read(database, ScalarIndexCrashContract.Partition, ScalarIndexCrashContract.SecondId);
        if (inserted is not null || second?.Revision != SecondRevision || second.Json != ScalarIndexCrashContract.PatchedJson)
        {
            throw new InvalidOperationException(ConflictMutationMessage);
        }
    }

    internal static void InsertEqualValueInOtherPartition(DatabaseEngine database)
        => _ = Apply(database, ScalarIndexCrashContract.Partition with
        { PartitionKey = ScalarIndexCrashContract.OtherPartitionKey }, ScalarIndexCrashContract.OtherPartitionCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.OtherPartitionId,
                ScalarIndexCrashContract.ReplacedJson, EmptyRevision));

    internal static async Task ApplyInflightCutAsync(DatabaseEngine database, ZoneTreeStore store,
        CanonicalCrashBoundary boundary)
    {
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        _ = Apply(database, ScalarIndexCrashContract.Partition, ScalarIndexCrashContract.FaultCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.ConflictId,
                ScalarIndexCrashContract.FaultInsertJson, EmptyRevision),
            new PatchDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.SecondId,
                [new(ScalarIndexCrashContract.LabelPath, PatchKind.Set, JsonSerializer.Serialize(ScalarIndexCrashContract.ValueTheta))], SecondRevision));
        await CrashHostPause.WaitForKillAsync();
    }

    internal static void ApplyHealthyFollowUp(DatabaseEngine database)
        => _ = Apply(database, ScalarIndexCrashContract.Partition, ScalarIndexCrashContract.HealthyCommandText,
            new PutDocument(ScalarIndexCrashContract.Collection, ScalarIndexCrashContract.FirstId,
                ScalarIndexCrashContract.HealthyJson, SecondRevision, ExplicitReplacement: true));

    internal static async Task WriteSnapshotAsync(string directory, string fileName, DatabaseEngine database)
    {
        var snapshot = Capture(database);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonDefaults.Options);
        if (bytes.Length is EmptyEvidenceBytes or > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        {
            throw new InvalidOperationException(EvidenceBoundMessage);
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }

    internal static ScalarIndexCrashSnapshot Capture(DatabaseEngine database)
    {
        var documents = new List<ScalarIndexDocumentState>();
        CapturePartition(database, ScalarIndexCrashContract.Partition, documents);
        CapturePartition(database, ScalarIndexCrashContract.OtherPartition, documents);
        var memberships = new List<ScalarIndexMembership>();
        var queryEngine = new QueryEngine(database, CrashExecutionOptions.QueryExecution());
        foreach (var partition in new[] { ScalarIndexCrashContract.Partition, ScalarIndexCrashContract.OtherPartition })
        {
            foreach (var value in Values)
            {
                var query = queryEngine.Execute(CrashFixtureValues.Principal,
                    new(partition, ScalarIndexCrashContract.QueryPrefix + value + ScalarIndexCrashContract.QuerySuffix));
                if (query.AccessPath != ScalarIndexCrashContract.IndexAccessPathPrefix + ScalarIndexCrashContract.IndexName)
                {
                    throw new InvalidOperationException(IndexPathMessage);
                }
                memberships.Add(new(partition.PartitionKey, value,
                    [.. query.Rows.Select(row => row.EntityId).OrderBy(static id => id, StringComparer.Ordinal)]));
            }
        }
        return new([.. documents], [.. memberships]);
    }

    private static void CapturePartition(DatabaseEngine database, PartitionRef partition, List<ScalarIndexDocumentState> documents)
    {
        foreach (var id in new[] { ScalarIndexCrashContract.FirstId, ScalarIndexCrashContract.SecondId,
            ScalarIndexCrashContract.DeletedId, ScalarIndexCrashContract.ConflictId, ScalarIndexCrashContract.OtherPartitionId })
        {
            var record = Read(database, partition, id);
            documents.Add(record is null
                ? MissingDocument(partition, id)
                : new(record.Reference.Partition.TenantId, record.Reference.Partition.DatabaseId,
                    record.Reference.Partition.TransactionDomainId, record.Reference.Partition.PartitionKey,
                    record.Reference.Collection, record.Reference.Id, record.Json, record.Revision, record.Deleted));
        }
    }

    private static ScalarIndexDocumentState MissingDocument(PartitionRef partition, string id)
        => new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            ScalarIndexCrashContract.Collection, id, null, EmptyRevision, false);

    private static DocumentRecord? Read(DatabaseEngine database, PartitionRef partition, string id)
        => database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(
            new EntityRef(partition, ScalarIndexCrashContract.Collection, id))));

    private static OperationResult Submit(DatabaseEngine database, string commandId, params Mutation[] mutations)
        => Apply(database, ScalarIndexCrashContract.Partition, commandId, mutations);

    private static OperationResult Apply(DatabaseEngine database, PartitionRef partition, string commandId,
        params Mutation[] mutations)
    {
        var id = Guid.Parse(commandId);
        var request = new CommandRequest(id, partition, [.. mutations]);
        return CrashDatabase.Submit(database, OperationKind.Batch, request, id);
    }
}
