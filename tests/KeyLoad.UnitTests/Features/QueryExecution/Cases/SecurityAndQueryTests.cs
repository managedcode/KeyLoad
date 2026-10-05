using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SecurityAndQueryTests
{
    private static void Reader(TestDatabase db, bool revoked = false, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], [])
        { Revoked = revoked, PolicyEpoch = epoch })).Get<PrincipalRecord>();
    [Test]
    public async Task NestedSensitiveFieldsAreOmittedAndAliasedPredicateAndSortAreDenied()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/people/*/secret", "pii"), new("/odd.name", "pii")]);
        db.Commit(new PutDocument("orders", "o1", "{\"people\":[{\"name\":\"A\",\"secret\":\"CANARY\"}],\"odd.name\":\"CANARY\",\"status\":\"open\"}"));
        Reader(db);
        var document = db.Database.GetDocument("reader", new(db.Partition, "orders", "o1"))!;
        await Assert.That(document.Redacted).IsTrue();
        await Assert.That(document.Json).DoesNotContain("CANARY");
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var result = queries.Execute("reader", new(db.Partition, "SELECT d.status AS state FROM orders d WHERE d.id = 'o1'"));
        await Assert.That(System.Linq.Enumerable.Single(result.Rows).Json).IsEqualTo("{\"state\":\"open\"}");
        await Assert.That(result.Rows[0].Redacted).IsTrue();
        await Assert.That(result.Rows[0].RedactedFields!.Value).Contains("/odd.name");
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders d WHERE d.\"odd.name\" = 'CANARY'", AllowFullScan: true)));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders ORDER BY \"odd.name\"", AllowFullScan: true))).Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
    [Test]
    public async Task RevocationInvalidatesCurrentPageAndNeverReturnsCachedPayload()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        Reader(db);
        var request = new QueryRequest(db.Partition, "SELECT * FROM orders LIMIT 1", AllowFullScan: true);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var first = engine.Execute("reader", request);
        await Assert.That(first.Cursor).IsNotNull();
        Reader(db, true, 2);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("reader", request with { Cursor = first.Cursor })).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }
    [Test]
    public async Task RowScopeAndTenantCannotBeForged()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "mine", "{}", Access: new("alice")), new PutDocument("orders", "hidden", "{}", Access: new("bob")));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("alice", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], [])
        { OwnerId = "alice", RestrictRows = true })).Get<PrincipalRecord>();
        var query = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()).Execute("alice", new(db.Partition, "SELECT * FROM orders", AllowFullScan: true));
        await Assert.That(System.Linq.Enumerable.Single(query.Rows).EntityId).IsEqualTo("mine");
        await Assert.That(db.Database.GetDocument("alice", new(db.Partition, "orders", "hidden"))).IsNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument("alice",
            new(db.Partition with { TenantId = "other" }, "orders", "mine"))).Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
    [Test]
    public async Task CompactionPreservesCursorAndReplicaInstallInvalidatesItsGeneration()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(db.Partition, "SELECT * FROM orders LIMIT 1", AllowFullScan: true);
        var first = engine.Execute("root", request);
        await Assert.That(first.Cursor).IsNotNull();
        var path = Path.Combine(db.Directory, "replica.snapshot");
        db.Store.CreateSnapshot(path);
        db.Store.Compact();
        await Assert.That(System.Linq.Enumerable.Single(engine.Execute("root", request with { Cursor = first.Cursor }).Rows).EntityId).IsEqualTo("b");
        db.Store.InstallSnapshot(path, 0);
        await Assert.That(db.Store.Position).IsEqualTo(first.CutPosition);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", request with { Cursor = first.Cursor })).Code).IsEqualTo(ErrorCode.CursorExpired);
    }
    [Test]
    public async Task SqlUsesTypedParametersThreeValuedNullAndDeterministicOrdering()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        db.Commit(new PutDocument("orders", "b", "{\"status\":\"open\",\"n\":null}"), new PutDocument("orders", "a", "{\"status\":\"open\",\"n\":2}"),
            new PutDocument("orders", "c", "{\"status\":\"closed\"}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var parameters = new Dictionary<string, JsonElement> { [QueryAdapterTestTokens.Status] = JsonSerializer.SerializeToElement("open") };
        var rows = engine.Execute("root", new(db.Partition, "SELECT d.id, d.n FROM orders d WHERE d.status = @status AND NOT d.n = 3 ORDER BY d.id", parameters));
        await Assert.That(System.Linq.Enumerable.Single(rows.Rows).EntityId).IsEqualTo("a");
        await Assert.That(rows.AccessPath).IsEqualTo("index:status");
        await Assert.That(System.Linq.Enumerable.Single(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status = 'open' AND n IS NULL")).Rows).EntityId).IsEqualTo("b");
        await Assert.That(System.Linq.Enumerable.Single(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE n IS MISSING", AllowFullScan: true)).Rows).EntityId).IsEqualTo("c");
        await Assert.That(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status = 'open' AND status = 'closed'")).Rows).IsEmpty();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", new(db.Partition, "SELECT * FROM orders"))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser("SELECT * FROM orders; DELETE FROM orders", new()).Parse()).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }
    [Test]
    public async Task WorkerRequiredProtectedInputFailsBeforeClaim()
    {
        using var db = new TestDatabase();
        db.Configure("jobs", ResourceKind.WorkQueue, fields: [new("/secret", "pii", RequiredForProcessing: true)]);
        db.Commit(new EnqueueMessage("jobs", "m1", "{\"secret\":\"CANARY\"}"));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("worker", "tenant", [new("database", "jobs", Capability.QueueConsume | Capability.QueueInspect)], []))).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var lane = new QueueLaneRef(db.Partition, "jobs");
        await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(id, lane), "worker", id).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.InspectMessage("root", lane, "m1")!.Metadata.State).IsEqualTo(MessageState.Ready);
    }
}
