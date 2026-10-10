using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalAtomicityTests
{
    private const string Documents = "documents";
    private const string Events = "events";
    private const string Queue = "jobs";
    private const string EventId = "created";
    private const string EventType = "Created";
    private const string QueryByName = "SELECT * FROM \"typed-rows\" WHERE name = 'alpha'";
    private const string IndexAccess = "index:by-name";
    private const string InvalidRow = "{\"key\":\"second\"}";
    private const string Alpha = "alpha";
    private const string Beta = "beta";
    private const string QuotedBeta = "\"beta\"";
    private const string Gamma = "\"gamma\"";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql004ConstraintFailureRollsBackEveryEarlierMixedModelEffect(bool uniqueConflict)
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        database.Configure(Documents, ResourceKind.Collection);
        database.Configure(Events, ResourceKind.StreamSet);
        database.Configure(Queue, ResourceKind.WorkQueue);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()));
        var row = uniqueConflict ? RelationalTestData.Row(RelationalTestData.Second) : InvalidRow;
        var failed = RelationalTestData.Submit(database,
            new PutDocument(Documents, RelationalTestData.Second, RelationalTestData.EmptyJson),
            new AppendEvents(Events, RelationalTestData.Second, [new(EventId, EventType, RelationalTestData.EmptyJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, RelationalTestData.Second, RelationalTestData.EmptyJson),
            new PutDocument(RelationalTestData.Table, RelationalTestData.Second, row));

        await Assert.That(failed.Error).IsEqualTo(uniqueConflict ? ErrorCode.Conflict : ErrorCode.Validation);
        await Assert.That(database.Database.GetDocument(RelationalTestData.Root, new(database.Partition, Documents, RelationalTestData.Second))).IsNull();
        await Assert.That(database.Database.GetDocument(RelationalTestData.Root, new(database.Partition, RelationalTestData.Table, RelationalTestData.Second))).IsNull();
        await Assert.That(database.Database.ReadStream(RelationalTestData.Root, new StreamRef(database.Partition, Events, RelationalTestData.Second)).Events).IsEmpty();
        await Assert.That(database.Database.InspectMessage(RelationalTestData.Root, new(database.Partition, Queue), RelationalTestData.Second)).IsNull();
        await Assert.That(database.Database.GetOutboxStatus(RelationalTestData.Root, database.Partition).Head.Tail).IsEqualTo(1);
        var query = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).Execute(RelationalTestData.Root, new(database.Partition, QueryByName));
        await Assert.That(query.Rows).HasSingleItem();
        await Assert.That(query.AccessPath).IsEqualTo(IndexAccess);
    }

    [Test]
    public async Task AcAisql004PatchConflictAndDeleteRetainNativeUniqueAndRevisionContract()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()),
            new PutDocument(RelationalTestData.Table, RelationalTestData.Second, RelationalTestData.Row(RelationalTestData.Second).Replace(Alpha, Beta, StringComparison.Ordinal)));
        var conflicting = RelationalTestData.Submit(database, new PatchDocument(RelationalTestData.Table, RelationalTestData.First,
            [new(RelationalTestData.NamePath, PatchKind.Set, QuotedBeta)], 1));
        await Assert.That(conflicting.Error).IsEqualTo(ErrorCode.Conflict);
        var stale = RelationalTestData.Submit(database, new PatchDocument(RelationalTestData.Table, RelationalTestData.First,
            [new(RelationalTestData.NamePath, PatchKind.Set, Gamma)], 0));
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.RevisionConflict);
        database.Commit(new DeleteDocument(RelationalTestData.Table, RelationalTestData.First, 1));
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.Second, RelationalTestData.Row(RelationalTestData.Second), 1));
        var rows = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).Execute(RelationalTestData.Root, new(database.Partition, QueryByName));
        await Assert.That(rows.Rows[0].EntityId).IsEqualTo(RelationalTestData.Second);
        await Assert.That(rows.Rows[0].Revision).IsEqualTo(2);
    }

    [Test]
    public async Task AcAisql004PersistedSchemaContinuesEnforcingRowsAfterRealStoreReopen()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()));
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var configured = reopened.Read(view => engine.Resource(view, database.Partition, RelationalTestData.Table));
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RelationalTestData.Key);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(RelationalTestData.Table, RelationalTestData.Second, InvalidRow)]);
        var result = engine.Apply(new(id, OperationKind.Batch, RelationalTestData.Root, TimeProvider.System.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options)));
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(engine.GetDocument(RelationalTestData.Root, new(database.Partition, RelationalTestData.Table, RelationalTestData.First))!.Revision).IsEqualTo(1);
        await Assert.That(engine.GetDocument(RelationalTestData.Root, new(database.Partition, RelationalTestData.Table, RelationalTestData.Second))).IsNull();
    }
}
