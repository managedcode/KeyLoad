using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.Authorization;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryAdapterValidationTests
{

    [Test]
    public async Task AcMp003SqlCancellationInterruptsAnUnterminatedLongLiteralBeforeSyntaxFailure()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        using var cancellation = new CancellationTokenSource();
        var sql = "SELECT * FROM orders WHERE status = '" + new string('x', 8_192);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());

        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root",
            new(db.Partition, sql), new CancelDuringSqlLexingTimeProvider(cancellation), cancellation.Token));

        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task AstRejectsUnknownVersionOperatorsAndNonScalarParametersBeforeScanning()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = new SelectQuery("orders", null, [new("*", "*")], null, [], 10);
        var request = new AstQueryRequest(db.Partition, query, AllowFullScan: true);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { AstVersion = 3 })).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var wrong = request with
        {
            Query = query with
            {
                Filter = new Logical(new Comparison(new FieldOperand("/number"), "=", ValueOperand.Create(1)),
            "XOR", new Comparison(new FieldOperand("/number"), "=", ValueOperand.Create(2)))
            }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", wrong)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var parameters = request with
        {
            Parameters = new()
            {
                [QueryAdapterTestTokens.BadParameter] = JsonSerializer.SerializeToElement(new[] { 1, 2 })
            }
        };
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
    public async Task InvalidCursorIsRejectedBeforeACollectionScan()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(new DatabaseEngine(db.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var request = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).ToRequest(true, "invalid-cursor");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request)).Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(engine.ExecuteAst("root", request with { Cursor = null }).Rows.Length).IsEqualTo(2);
    }

    [Test]
    public async Task CatalogAndOtherCollectionWritesPreserveTheCursorButSourceWritesInvalidateIt()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("other", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).Take(1).ToRequest(true);
        var first = engine.ExecuteAst("root", request);
        db.Submit(OperationKind.SetDispatch, true).Get<bool>();
        db.Commit(new PutDocument("other", "unrelated", "{}"));
        var continued = engine.ExecuteAst("root", request with
        {
            Cursor = first.Cursor
        });
        await Assert.That(System.Linq.Enumerable.Single(continued.Rows).EntityId).IsEqualTo("b");
        await Assert.That(continued.CutPosition).IsEqualTo(first.CutPosition);
        db.Commit(new PatchDocument("orders", "a", [new("/changed", PatchKind.Set, "true")], 1));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root", request with { Cursor = first.Cursor })).Code).IsEqualTo(ErrorCode.CursorExpired);
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
