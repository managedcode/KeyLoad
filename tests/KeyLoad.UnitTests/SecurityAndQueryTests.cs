using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests;

public sealed class SecurityAndQueryTests
{
    private static void Reader(TestDatabase db, bool revoked = false, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], []) { Revoked = revoked, PolicyEpoch = epoch })).Get<PrincipalRecord>();
    [Fact]
    public void NestedSensitiveFieldsAreOmittedAndAliasedPredicateAndSortAreDenied()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/people/*/secret", "pii"), new("/odd.name", "pii")]);
        db.Commit(new PutDocument("orders", "o1", "{\"people\":[{\"name\":\"A\",\"secret\":\"CANARY\"}],\"odd.name\":\"CANARY\",\"status\":\"open\"}"));
        Reader(db);
        var document = db.Database.GetDocument("reader", new(db.Partition, "orders", "o1"))!;
        Assert.True(document.Redacted); Assert.DoesNotContain("CANARY", document.Json);
        var queries = new QueryEngine(db.Database);
        var result = queries.Execute("reader", new(db.Partition, "SELECT d.status AS state FROM orders d WHERE d.id = 'o1'"));
        Assert.Equal("{\"state\":\"open\"}", Assert.Single(result.Rows).Json);
        Assert.True(result.Rows[0].Redacted);
        Assert.Contains("/odd.name", result.Rows[0].RedactedFields!);
        var exception = Assert.Throws<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders d WHERE d.\"odd.name\" = 'CANARY'", AllowFullScan: true)));
        Assert.Equal(ErrorCode.PermissionDenied, exception.Code);
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders ORDER BY \"odd.name\"", AllowFullScan: true))).Code);
    }
    [Fact]
    public void RevocationInvalidatesCurrentPageAndNeverReturnsCachedPayload()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}")); Reader(db);
        var request = new QueryRequest(db.Partition, "SELECT * FROM orders LIMIT 1", AllowFullScan: true);
        var engine = new QueryEngine(db.Database); var first = engine.Execute("reader", request);
        Assert.NotNull(first.Cursor); Reader(db, true, 2);
        Assert.Equal(ErrorCode.Unauthenticated, Assert.Throws<KeyLoadException>(() => engine.Execute("reader", request with { Cursor = first.Cursor })).Code);
    }
    [Fact]
    public void RowScopeAndTenantCannotBeForged()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "mine", "{}", Access: new("alice")), new PutDocument("orders", "hidden", "{}", Access: new("bob")));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("alice", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], []) { OwnerId = "alice", RestrictRows = true })).Get<PrincipalRecord>();
        var query = new QueryEngine(db.Database).Execute("alice", new(db.Partition, "SELECT * FROM orders", AllowFullScan: true));
        Assert.Equal("mine", Assert.Single(query.Rows).EntityId);
        Assert.Null(db.Database.GetDocument("alice", new(db.Partition, "orders", "hidden")));
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => db.Database.GetDocument("alice",
            new(db.Partition with { TenantId = "other" }, "orders", "mine"))).Code);
    }
    [Fact]
    public void CompactionPreservesCursorAndReplicaInstallInvalidatesItsGeneration()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(db.Database);
        var request = new QueryRequest(db.Partition, "SELECT * FROM orders LIMIT 1", AllowFullScan: true);
        var first = engine.Execute("root", request);
        Assert.NotNull(first.Cursor);
        var path = Path.Combine(db.Directory, "replica.snapshot");
        db.Store.CreateSnapshot(path);
        db.Store.Compact();
        Assert.Equal("b", Assert.Single(engine.Execute("root", request with { Cursor = first.Cursor }).Rows).EntityId);
        db.Store.InstallSnapshot(path, 0);
        Assert.Equal(first.CutPosition, db.Store.Position);
        Assert.Equal(ErrorCode.CursorExpired, Assert.Throws<KeyLoadException>(() => engine.Execute("root", request with { Cursor = first.Cursor })).Code);
    }
    [Fact]
    public void SqlUsesTypedParametersThreeValuedNullAndDeterministicOrdering()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        db.Commit(new PutDocument("orders", "b", "{\"status\":\"open\",\"n\":null}"), new PutDocument("orders", "a", "{\"status\":\"open\",\"n\":2}"),
            new PutDocument("orders", "c", "{\"status\":\"closed\"}"));
        var engine = new QueryEngine(db.Database);
        var parameters = new Dictionary<string, JsonElement> { ["status"] = JsonSerializer.SerializeToElement("open") };
        var rows = engine.Execute("root", new(db.Partition, "SELECT d.id, d.n FROM orders d WHERE d.status = @status AND NOT d.n = 3 ORDER BY d.id", parameters));
        Assert.Equal("a", Assert.Single(rows.Rows).EntityId); Assert.Equal("index:status", rows.AccessPath);
        Assert.Equal("b", Assert.Single(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status = 'open' AND n IS NULL")).Rows).EntityId);
        Assert.Equal("c", Assert.Single(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE n IS MISSING", AllowFullScan: true)).Rows).EntityId);
        Assert.Empty(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status = 'open' AND status = 'closed'")).Rows);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => engine.Execute("root", new(db.Partition, "SELECT * FROM orders"))).Code);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => new SqlParser("SELECT * FROM orders; DELETE FROM orders", new()).Parse()).Code);
    }
    [Fact]
    public void WorkerRequiredProtectedInputFailsBeforeClaim()
    {
        using var db = new TestDatabase(); db.Configure("jobs", ResourceKind.WorkQueue, fields: [new("/secret", "pii", RequiredForProcessing: true)]);
        db.Commit(new EnqueueMessage("jobs", "m1", "{\"secret\":\"CANARY\"}"));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("worker", "tenant", [new("database", "jobs", Capability.QueueConsume | Capability.QueueInspect)], []))).Get<PrincipalRecord>();
        var id = Guid.NewGuid(); var lane = new QueueLaneRef(db.Partition, "jobs");
        Assert.Equal(ErrorCode.PermissionDenied, db.Submit(OperationKind.Receive, new ReceiveRequest(id, lane), "worker", id).Error);
        Assert.Equal(MessageState.Ready, db.Database.InspectMessage("root", lane, "m1")!.Metadata.State);
    }
}
