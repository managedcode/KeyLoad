using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryAdapterValidationTests
{
    [Test]
    public async Task AcMp003SqlDeadlineInterruptsAnUnterminatedLongLiteralBeforeSyntaxFailure()
    {
        using var db = new TestDatabase(new() { QueryDeadlineSeconds = 1 });
        db.Configure("orders", ResourceKind.Collection);
        var sql = "SELECT * FROM orders WHERE status = '" + new string('x', 8_192);
        var engine = new QueryEngine(db.Database);

        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(db.Partition, sql), new ExpireDuringSqlLexingTimeProvider()));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp003SqlCancellationInterruptsAnUnterminatedLongLiteralBeforeSyntaxFailure()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        using var cancellation = new CancellationTokenSource();
        var sql = "SELECT * FROM orders WHERE status = '" + new string('x', 8_192);
        var engine = new QueryEngine(db.Database);

        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root",
            new(db.Partition, sql), new CancelDuringSqlLexingTimeProvider(cancellation), cancellation.Token));

        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task AstRejectsUnknownVersionOperatorsNonScalarParametersAndBudgetsBeforeScanning()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database);
        var query = new SelectQuery("orders", null, [new("*", "*")], null, [], 10);
        var request = new AstQueryRequest(db.Partition, query, AllowFullScan: true);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { AstVersion = 2 })).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var wrong = request with
        {
            Query = query with
            {
                Filter = new Logical(new Comparison(new FieldOperand("/number"), "=", ValueOperand.Create(1)),
            "XOR", new Comparison(new FieldOperand("/number"), "=", ValueOperand.Create(2)))
            }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", wrong)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var parameters = request with { Parameters = new() { [QueryAdapterTestTokens.BadParameter] = JsonSerializer.SerializeToElement(new[] { 1, 2 }) } };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", parameters)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        foreach (var malformed in new[] { request with { Query = query with { Projection = [null!] } }, request with { Query = query with { Order = [null!] } } })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", QueryAdapterTestSupport.RoundTrip(malformed))).Code).IsEqualTo(ErrorCode.Validation);
        }
        foreach (var malformed in new[] { request with { Query = null! }, request with { Partition = null! },
            request with { Query = query with { Projection = [new(null!, "field")] } } })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", malformed)).Code).IsEqualTo(ErrorCode.Validation);
        }
        Predicate deep = new NullTest(new FieldOperand("/number"), false, false);
        for (var index = 0; index < 40; index++)
        {
            deep = new Negation(deep);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { Query = query with { Filter = deep } })).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var huge = request with { Query = query with { Filter = new Comparison(new FieldOperand("/status"), "=", ValueOperand.Create(new string('x', 70_000))) } };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", huge)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public void MalformedPolymorphicAstIsRejectedByTheProtocolDeserializer()
    {
        var request = new AstQueryRequest(new("tenant", "database", "domain", QueryAdapterTestTokens.Partition),
            new("orders", null, [new("*", "*")], new NullTest(new FieldOperand("/n"), false, false), [], 10));
        var json = System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(request)).Replace("\"kind\":\"nullTest\"", "\"kind\":\"script\"", StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<AstQueryRequest>(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Test]
    public async Task InvalidCursorIsRejectedBeforeACollectionScanUsesItsBudget()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(new DatabaseEngine(db.Store, new AuthorizationPolicy(), new() { MaxScanRecords = 1 }));
        var request = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders").ToRequest(true, "invalid-cursor");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request)).Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { Cursor = null })).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task IndexedDocumentLookupsChargeTheirBytesBeforeMaterializingTheCandidateSet()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        db.Commit(Enumerable.Range(0, 6).Select(index => (Mutation)new PutDocument("orders", "order-" + index,
            JsonSerializer.Serialize(new { status = "open", payload = new string('x', 500) }))).ToArray());
        var engine = new QueryEngine(new DatabaseEngine(db.Store, new AuthorizationPolicy(), new() { MaxQueryReadBytes = 1_500 }));
        var query = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders").Where(row => row.Status == "open").ToRequest();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", query)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var point = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders").Where(row => QueryFunctions.DocumentId(row) == "order-0").ToRequest();
        await Assert.That(engine.ExecuteAst("root", point).Rows).HasSingleItem();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.StartLiveQuery("root", new(query))).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task QueryResultByteBudgetIncludesIdentityAndRedactionMetadata()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/secret", "pii")]);
        db.Commit(new PutDocument("orders", new string('a', 200), "{\"secret\":\"CANARY\"}"));
        var engine = new QueryEngine(new DatabaseEngine(db.Store, new AuthorizationPolicy(), new() { MaxBatchBytes = 100 }));
        var query = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders").ToRequest(true);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", query)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task CatalogAndOtherCollectionWritesPreserveTheCursorButSourceWritesInvalidateIt()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("other", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(db.Database);
        var request = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders").Take(1).ToRequest(true);
        var first = engine.ExecuteAst("root", request);
        db.Submit(OperationKind.SetDispatch, true).Get<bool>();
        db.Commit(new PutDocument("other", "unrelated", "{}"));
        var continued = engine.ExecuteAst("root", request with { Cursor = first.Cursor });
        await Assert.That(System.Linq.Enumerable.Single(continued.Rows).EntityId).IsEqualTo("b");
        await Assert.That(continued.CutPosition).IsEqualTo(first.CutPosition);
        db.Commit(new PatchDocument("orders", "a", [new("/changed", PatchKind.Set, "true")], 1));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { Cursor = first.Cursor })).Code).IsEqualTo(ErrorCode.CursorExpired);
    }

    private sealed class ExpireDuringSqlLexingTimeProvider : TimeProvider
    {
        private const int ExpireOnTimestampCall = 4;
        private const long ExpiredTimestamp = 2 * TimeSpan.TicksPerSecond;
        private int timestampCalls;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
            => Interlocked.Increment(ref timestampCalls) >= ExpireOnTimestampCall ? ExpiredTimestamp : 0;
    }

    private sealed class CancelDuringSqlLexingTimeProvider(CancellationTokenSource cancellation) : TimeProvider
    {
        private const int CancelOnTimestampCall = 4;
        private int timestampCalls;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref timestampCalls) == CancelOnTimestampCall)
            {
                cancellation.Cancel();
            }

            return 0;
        }
    }
}
