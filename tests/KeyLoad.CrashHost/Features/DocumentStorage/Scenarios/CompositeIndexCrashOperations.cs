using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CompositeIndexCrashOperations
{
    private const string AcknowledgedRankPatchJson = "5";
    private const string InflightRankPatchJson = "8";
    private const string RankEqualitySqlFragment = " AND rank = ";
    private const long EmptyRevision = 0;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const int PositionStep = 1;
    private const int EmptyEvidenceBytes = 0;
    private const string EvidenceBoundMessage = "The composite-index process evidence exceeded its existing fixed bound.";
    private const string IndexPathMessage = "Scalar-index recovery observation did not use the declared native index.";
    private static readonly string[] Values = [CompositeIndexCrashContract.ValueAlpha, CompositeIndexCrashContract.ValueBeta,
        CompositeIndexCrashContract.ValueGamma, CompositeIndexCrashContract.ValueDelta, CompositeIndexCrashContract.ValueEpsilon,
        CompositeIndexCrashContract.ValueRollback, CompositeIndexCrashContract.ValueZeta,
        CompositeIndexCrashContract.ValueTheta, CompositeIndexCrashContract.ValueHealthy];

    internal static void Configure(DatabaseEngine database)
    {
        var definition = new ResourceDefinition(CompositeIndexCrashContract.Collection, ResourceKind.Collection,
            CompositeIndexCrashContract.Partition.TransactionDomainId)
        {
            Indexes = [new(CompositeIndexCrashContract.IndexName, [CompositeIndexCrashContract.LabelPath, CompositeIndexCrashContract.RankPath], Unique: true),
            new(CompositeIndexCrashContract.RankIndex, [CompositeIndexCrashContract.RankPath])]
        };
        var request = new ConfigureResourceRequest(CompositeIndexCrashContract.Partition.TenantId,
            CompositeIndexCrashContract.Partition.DatabaseId, definition);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, request,
            Guid.Parse(CompositeIndexCrashContract.CollectionCommandText)).Get<ResourceDefinition>();
    }

    internal static void Seed(DatabaseEngine database)
        => _ = Submit(database, CompositeIndexCrashContract.SeedCommandText,
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.FirstId,
                CompositeIndexCrashContract.FirstInitialJson, EmptyRevision),
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.SecondId,
                CompositeIndexCrashContract.SecondInitialJson, EmptyRevision),
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.DeletedId,
                CompositeIndexCrashContract.DeletedInitialJson, EmptyRevision)).Get<CommitReceipt>();

    internal static void ApplyAcknowledgedMutations(DatabaseEngine database)
    {
        _ = Submit(database, CompositeIndexCrashContract.ReplaceCommandText,
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.FirstId,
                CompositeIndexCrashContract.ReplacedJson, FirstRevision, ExplicitReplacement: true)).Get<CommitReceipt>();
        _ = Submit(database, CompositeIndexCrashContract.PatchCommandText,
            new PatchDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.SecondId,
                [new(CompositeIndexCrashContract.LabelPath, PatchKind.Set, JsonSerializer.Serialize(CompositeIndexCrashContract.ValueEpsilon)),
                new(CompositeIndexCrashContract.RankPath, PatchKind.Set, AcknowledgedRankPatchJson)], FirstRevision)).Get<CommitReceipt>();
        _ = Submit(database, CompositeIndexCrashContract.DeleteCommandText,
            new DeleteDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.DeletedId, FirstRevision)).Get<CommitReceipt>();
    }

    internal static void InsertEqualValueInOtherPartition(DatabaseEngine database)
        => _ = Apply(database, CompositeIndexCrashContract.Partition with
        { PartitionKey = CompositeIndexCrashContract.OtherPartitionKey }, CompositeIndexCrashContract.OtherPartitionCommandText,
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.OtherPartitionId,
                CompositeIndexCrashContract.ReplacedJson, EmptyRevision)).Get<CommitReceipt>();

    internal static async Task ApplyInflightCutAsync(DatabaseEngine database, ZoneTreeStore store,
        CanonicalCrashBoundary boundary)
    {
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        _ = Apply(database, CompositeIndexCrashContract.Partition, CompositeIndexCrashContract.FaultCommandText,
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.ConflictId,
                CompositeIndexCrashContract.FaultInsertJson, EmptyRevision),
            new PatchDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.SecondId,
                [new(CompositeIndexCrashContract.LabelPath, PatchKind.Set, JsonSerializer.Serialize(CompositeIndexCrashContract.ValueTheta)),
                new(CompositeIndexCrashContract.RankPath, PatchKind.Set, InflightRankPatchJson)], SecondRevision)).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static void ApplyHealthyFollowUp(DatabaseEngine database)
        => _ = Apply(database, CompositeIndexCrashContract.Partition, CompositeIndexCrashContract.HealthyCommandText,
            new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.FirstId,
                CompositeIndexCrashContract.HealthyJson, SecondRevision, ExplicitReplacement: true)).Get<CommitReceipt>();

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

    internal static CompositeIndexCrashSnapshot Capture(DatabaseEngine database)
    {
        var documents = new List<CompositeIndexDocumentState>();
        CapturePartition(database, CompositeIndexCrashContract.Partition, documents);
        CapturePartition(database, CompositeIndexCrashContract.OtherPartition, documents);
        var memberships = new List<CompositeIndexMembership>();
        var queryEngine = new QueryEngine(database, CrashExecutionOptions.QueryExecution());
        foreach (var partition in new[] { CompositeIndexCrashContract.Partition, CompositeIndexCrashContract.OtherPartition })
        {
            foreach (var value in Values)
            {
                var query = queryEngine.Execute(CrashFixtureValues.Principal,
                    new(partition, CompositeIndexCrashContract.QueryPrefix + value + CompositeIndexCrashContract.QuerySuffix
                        + RankEqualitySqlFragment + (Array.IndexOf(Values, value) + PositionStep).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (query.AccessPath != CompositeIndexCrashContract.IndexAccessPathPrefix + CompositeIndexCrashContract.IndexName)
                {
                    throw new InvalidOperationException(IndexPathMessage);
                }
                memberships.Add(new(partition.PartitionKey, value,
                    [.. query.Rows.Select(row => row.EntityId).OrderBy(static id => id, StringComparer.Ordinal)]));
            }
        }
        return new([.. documents], [.. memberships], CompositePhysicalIndexObservation.Capture(database));
    }

    private static void CapturePartition(DatabaseEngine database, PartitionRef partition, List<CompositeIndexDocumentState> documents)
    {
        foreach (var id in new[] { CompositeIndexCrashContract.FirstId, CompositeIndexCrashContract.SecondId,
            CompositeIndexCrashContract.DeletedId, CompositeIndexCrashContract.ConflictId, CompositeIndexCrashContract.OtherPartitionId })
        {
            var record = Read(database, partition, id);
            documents.Add(record is null
                ? MissingDocument(partition, id)
                : new(record.Reference.Partition.TenantId, record.Reference.Partition.DatabaseId,
                    record.Reference.Partition.TransactionDomainId, record.Reference.Partition.PartitionKey,
                    record.Reference.Collection, record.Reference.Id, record.Json, record.Revision, record.Deleted));
        }
    }

    private static CompositeIndexDocumentState MissingDocument(PartitionRef partition, string id)
        => new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            CompositeIndexCrashContract.Collection, id, null, EmptyRevision, false);

    private static DocumentRecord? Read(DatabaseEngine database, PartitionRef partition, string id)
        => database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(
            new EntityRef(partition, CompositeIndexCrashContract.Collection, id))));

    private static OperationResult Submit(DatabaseEngine database, string commandId, params Mutation[] mutations)
        => Apply(database, CompositeIndexCrashContract.Partition, commandId, mutations);

    private static OperationResult Apply(DatabaseEngine database, PartitionRef partition, string commandId,
        params Mutation[] mutations)
    {
        var id = Guid.Parse(commandId);
        var request = new CommandRequest(id, partition, [.. mutations]);
        return CrashDatabase.Submit(database, OperationKind.Batch, request, id);
    }
}
