using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SecurityAndQueryTests
{
    private const string ProtectedProjectionSql = "SELECT d.[odd.name] AS hidden FROM orders d WHERE d.id = 'o1'";
    private const string ProtectedProjectionJson = "{\"hidden\":null}";
    private const string ProtectedProjectionPath = "/odd.name";

    private static void Reader(TestDatabase db, bool revoked = false, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], [])
        {
            Revoked = revoked,
            PolicyEpoch = epoch
        })).Get<PrincipalRecord>();
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
        var position = db.Store.Position;
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var result = queries.Execute("reader", new(db.Partition, "SELECT d.status AS state FROM orders d WHERE d.id = 'o1'"));
        await Assert.That(System.Linq.Enumerable.Single(result.Rows).Json).IsEqualTo("{\"state\":\"open\"}");
        await Assert.That(result.Rows[0].Redacted).IsFalse();
        await Assert.That(result.Rows[0].RedactedFields!.Value).IsEmpty();
        var protectedPage = queries.Execute("reader", new(db.Partition, ProtectedProjectionSql));
        var protectedRow = protectedPage.Rows.Single();
        await Assert.That(protectedRow.EntityId).IsEqualTo(document.Reference.Id);
        await Assert.That(protectedRow.Revision).IsEqualTo(document.Revision);
        await Assert.That(protectedRow.Json).IsEqualTo(ProtectedProjectionJson);
        await Assert.That(protectedRow.Redacted).IsTrue();
        await Assert.That(protectedRow.RedactedFields!.Value.SequenceEqual([ProtectedProjectionPath])).IsTrue();
        await Assert.That(protectedPage.CutPosition).IsEqualTo(result.CutPosition);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders d WHERE d.\"odd.name\" = 'CANARY'", AllowFullScan: true)));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute("reader",
            new(db.Partition, "SELECT * FROM orders ORDER BY \"odd.name\"", AllowFullScan: true))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await KeyLoad.FunctionalTests.Authorization.NestedSensitiveCurrentPolicyContinuation.RunAsync(db,
            TestContext.Current!.Execution.CancellationToken);
    }
    [Test]
    public async Task RevocationInvalidatesCurrentPageAndNeverReturnsCachedPayload()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        Reader(db);
        var request = new QueryRequest(db.Partition, "SELECT TOP (1) * FROM orders", AllowFullScan: true);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var first = engine.Execute("reader", request);
        await Assert.That(first.Cursor).IsNotNull();
        Reader(db, true, 2);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("reader", request with { Cursor = first.Cursor })).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await KeyLoad.FunctionalTests.Authorization.PolicyEpochCursorContinuation.RunAsync(db, request, first.Cursor!,
            TestContext.Current!.Execution.CancellationToken);
    }
    [Test]
    public async Task RowScopeAndTenantCannotBeForged()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "mine", "{}", Access: new("alice")), new PutDocument("orders", "hidden", "{}", Access: new("bob")));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("alice", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], [])
        {
            OwnerId = "alice",
            RestrictRows = true
        })).Get<PrincipalRecord>();
        var query = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()).Execute("alice", new(db.Partition, "SELECT * FROM orders", AllowFullScan: true));
        await Assert.That(System.Linq.Enumerable.Single(query.Rows).EntityId).IsEqualTo("mine");
        await Assert.That(db.Database.GetDocument("alice", new(db.Partition, "orders", "hidden"))).IsNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument("alice",
            new(db.Partition with
            {
                TenantId = "other"
            }, "orders", "mine"))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await ProjectRowAuthorityContinuation.RunAsync(db, TestContext.Current!.Execution.CancellationToken);
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
        var missing = new AstQueryRequest(db.Partition, new("orders", null, [new("*", "*")],
            new NullTest(new FieldOperand("/n"), false, true), [], 100), AllowFullScan: true);
        await Assert.That(System.Linq.Enumerable.Single(engine.ExecuteAst("root", missing).Rows).EntityId).IsEqualTo("c");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(db.Partition, "SELECT * FROM orders WHERE n IS MISSING", AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status = 'open' AND status = 'closed'")).Rows).IsEmpty();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", new(db.Partition, "SELECT * FROM orders"))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser("SELECT * FROM orders; DELETE FROM orders", UnitExecutionOptions.QueryExecution()).Parse()).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task SelectedJsonPreservesOrderEscapingAndScalarRepresentations()
    {
        const string Source = """{"status":"é<","amount":1.2300,"flag":true,"none":null}""";
        const string Expected = """{"z":"\u00E9\u003C","a":1.2300,"flag":true,"none":null,"absent":null}""";
        const string Shorter = """{"z":null,"a":null,"flag":null,"none":null,"absent":null}""";
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "o1", Source), new PutDocument("orders", "o2", "{}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = new SelectQuery("orders", null,
            [new("/status", "z"), new("/amount", "a"), new("/flag", "flag"), new("/none", "none"), new("/absent", "absent")],
            null, [], 2);

        var rows = engine.ExecuteAst("root", new(db.Partition, query, AllowFullScan: true)).Rows;

        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(rows[0].Json).IsEqualTo(Expected);
        await Assert.That(rows[1].Json).IsEqualTo(Shorter);
        await Assert.That(rows[0].Redacted).IsFalse();
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
        await Assert.That(Errors.Code(db.Submit(OperationKind.Receive, new ReceiveRequest(id, lane), "worker", id).Problem).Value).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.InspectMessage("root", lane, "m1")!.Metadata.State).IsEqualTo(MessageState.Ready);
        await KeyLoad.FunctionalTests.Authorization.EventingRequiredClaimFlow.RunAllAsync(
            TestContext.Current!.Execution.CancellationToken);
    }
}
