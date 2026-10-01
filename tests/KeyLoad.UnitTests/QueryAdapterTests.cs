using System.Text.Json;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;

namespace KeyLoad.UnitTests;

public sealed class QueryAdapterTests
{
    private sealed record Order(decimal Number, string Status)
    {
        [JsonPropertyName("odd.name")] public string? Secret { get; init; }
        [JsonPropertyName("n")] public decimal? Optional { get; init; }
    }
    private sealed class UnsafeConstant
    {
        public int Calls;
        public string Value { get { Calls++; throw new InvalidOperationException("Application code must not execute."); } }
    }
    private static AstQueryRequest RoundTrip(AstQueryRequest request) => JsonDefaults.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request));
    private static void SameRows(QueryPage expected, QueryPage actual)
        => Assert.Equal(JsonDefaults.Serialize(expected.Rows), JsonDefaults.Serialize(actual.Rows));
    [Fact]
    public void SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        var random = new Random(9173); var statuses = new[] { "open", "hold", "closed" };
        db.Commit(Enumerable.Range(0, 200).Select(index => new PutDocument("orders", "doc-" + index.ToString("D3"),
            JsonSerializer.Serialize(new Order(random.Next(20), statuses[random.Next(statuses.Length)]), JsonDefaults.Options))).Cast<Mutation>().ToArray());
        var engine = new QueryEngine(db.Database);
        for (var trial = 0; trial < 50; trial++)
        {
            var minimum = (decimal)random.Next(20); var status = statuses[random.Next(statuses.Length)];
            var sql = $"SELECT d.id, d.number, d.status FROM orders d WHERE d.status = '{status}' AND d.number >= {minimum} ORDER BY d.number DESC, d.id LIMIT 30";
            var expected = engine.Execute("root", new(db.Partition, sql));
            var json = engine.ExecuteAst("root", RoundTrip(new(db.Partition, new SqlParser(sql, new()).Parse())));
            var csharp = KeyLoadQuery<Order>.From(db.Partition, "orders").Where(row => row.Status == status && row.Number >= minimum)
                .OrderByDescending(row => row.Number).ThenBy(row => QueryFunctions.DocumentId(row))
                .Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number, row.Status }).Take(30);
            var actual = engine.ExecuteAst("root", RoundTrip(csharp.ToRequest()));
            SameRows(expected, json); SameRows(expected, actual);
            Assert.Equal("index:status", actual.AccessPath); Assert.Equal(expected.AccessPath, json.AccessPath);
        }
    }
    [Fact]
    public void CursorContinuesAcrossEquivalentSqlJsonAndCSharpForms()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"number\":2}"), new PutDocument("orders", "b", "{\"number\":3}"), new PutDocument("orders", "c", "{\"number\":4}"));
        var engine = new QueryEngine(db.Database);
        const string sql = "SELECT d.id, d.number FROM orders d WHERE d.number >= 2.00 ORDER BY d.id LIMIT 1";
        var first = engine.Execute("root", new(db.Partition, sql, AllowFullScan: true)); Assert.Equal("a", Assert.Single(first.Rows).EntityId);
        var json = RoundTrip(new(db.Partition, new SqlParser(sql.Replace("2.00", "2", StringComparison.Ordinal), new()).Parse(),
            new(), true, first.Cursor));
        var second = engine.ExecuteAst("root", json); Assert.Equal("b", Assert.Single(second.Rows).EntityId);
        var builder = KeyLoadQuery<Order>.From(db.Partition, "orders").Where(row => row.Number >= 2m)
            .OrderBy(row => QueryFunctions.DocumentId(row)).Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number }).Take(1);
        var third = engine.ExecuteAst("root", RoundTrip(builder.ToRequest(true, second.Cursor)));
        Assert.Equal("c", Assert.Single(third.Rows).EntityId); Assert.Null(third.Cursor);
    }
    [Fact]
    public void EveryAdapterChecksSensitivePredicateAndOrderingAndProjectsTheSameOmission()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, fields: [new("/odd.name", "pii")]);
        db.Commit(new PutDocument("orders", "a", "{\"odd.name\":\"CANARY\",\"number\":1,\"status\":\"open\"}"));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.Query | Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        var engine = new QueryEngine(db.Database);
        var filter = KeyLoadQuery<Order>.From(db.Partition, "orders").Where(row => row.Secret == "CANARY").ToRequest(true);
        var sort = KeyLoadQuery<Order>.From(db.Partition, "orders").OrderBy(row => row.Secret).ToRequest(true);
        foreach (var query in new[] { filter, sort })
            Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("reader", RoundTrip(query))).Code);
        var safe = engine.ExecuteAst("reader", RoundTrip(KeyLoadQuery<Order>.From(db.Partition, "orders").ToRequest(true)));
        var sql = engine.Execute("reader", new(db.Partition, "SELECT * FROM orders", AllowFullScan: true));
        SameRows(sql, safe); Assert.DoesNotContain("CANARY", Assert.Single(safe.Rows).Json);
    }
    [Fact]
    public void NullMissingAndInHaveExplicitCanonicalSemantics()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "null", "{\"n\":null,\"status\":\"open\"}"), new PutDocument("orders", "missing", "{\"status\":\"hold\"}"),
            new PutDocument("orders", "value", "{\"n\":2,\"status\":\"closed\"}"));
        var engine = new QueryEngine(db.Database); var set = new[] { "open", "hold" };
        Expression<Func<Order, bool>> filter = row => set.Contains(row.Status)
            && (QueryFunctions.IsMissing(row.Optional) || QueryFunctions.IsNull(row.Optional));
        var query = KeyLoadQuery<Order>.From(db.Partition, "orders").Where(filter).OrderBy(row => QueryFunctions.DocumentId(row));
        var actual = engine.ExecuteAst("root", RoundTrip(query.ToRequest(true)));
        var sql = engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status IN ('open','hold') AND (n IS MISSING OR n IS NULL) ORDER BY id", AllowFullScan: true));
        SameRows(sql, actual); Assert.Equal(new[] { "missing", "null" }, actual.Rows.Select(row => row.EntityId));
    }
    [Fact]
    public void UnsupportedExpressionsNeverInvokeApplicationGettersOrDelegates()
    {
        var query = KeyLoadQuery<Order>.From(new("tenant", "database", "domain", "partition"), "orders");
        var constant = new UnsafeConstant();
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => query.Where(row => row.Status == constant.Value)).Code);
        Assert.Equal(0, constant.Calls);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => query.Where(row => row.Status.StartsWith("o"))).Code);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => query.Where(row => (int)row.Number > 2)).Code);
    }
    [Fact]
    public void BuilderBranchesHaveIndependentQueryContexts()
    {
        var original = KeyLoadQuery<Order>.From(new("tenant", "database", "domain", "partition"), "orders");
        var a = original.Where(row => row.Number > 3m).OrderBy(row => row.Number).Take(1);
        var b = original.Where(row => row.Status == "open").OrderByDescending(row => row.Status).Take(2);
        Assert.Null(original.ToRequest().Query.Filter); Assert.Empty(original.ToRequest().Query.Order); Assert.Equal(100, original.ToRequest().Query.Limit);
        Assert.Equal(1, a.ToRequest().Query.Limit); Assert.Equal(2, b.ToRequest().Query.Limit);
        Assert.Equal("/number", Assert.Single(a.ToRequest().Query.Order).Path);
        Assert.Equal("/status", Assert.Single(b.ToRequest().Query.Order).Path);
    }
    [Fact]
    public void AstRejectsUnknownVersionOperatorsNonScalarParametersAndBudgetsBeforeScanning()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); var engine = new QueryEngine(db.Database);
        var query = new SelectQuery("orders", null, [new("*", "*")], null, [], 10); var request = new AstQueryRequest(db.Partition, query, AllowFullScan: true);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", request with { AstVersion = 2 })).Code);
        var wrong = request with { Query = query with { Filter = new Logical(new Comparison(new FieldOperand("/number"), "=", new ValueOperand(1)),
            "XOR", new Comparison(new FieldOperand("/number"), "=", new ValueOperand(2))) } };
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", wrong)).Code);
        var parameters = request with { Parameters = new() { ["bad"] = JsonSerializer.SerializeToElement(new[] { 1, 2 }) } };
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", parameters)).Code);
        foreach (var malformed in new[] { request with { Query = query with { Projection = [null!] } }, request with { Query = query with { Order = [null!] } } })
            Assert.Equal(ErrorCode.Validation, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", RoundTrip(malformed))).Code);
        foreach (var malformed in new[] { request with { Query = null! }, request with { Partition = null! },
            request with { Query = query with { Projection = [new(null!, "field")] } } })
            Assert.Equal(ErrorCode.Validation, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", malformed)).Code);
        Predicate deep = new NullTest(new FieldOperand("/number"), false, false);
        for (var index = 0; index < 40; index++) deep = new Negation(deep);
        Assert.Equal(ErrorCode.BudgetExceeded, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", request with { Query = query with { Filter = deep } })).Code);
        var huge = request with { Query = query with { Filter = new Comparison(new FieldOperand("/status"), "=", new ValueOperand(new string('x', 70_000))) } };
        Assert.Equal(ErrorCode.BudgetExceeded, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", huge)).Code);
    }
    [Fact]
    public void MalformedPolymorphicAstIsRejectedByTheProtocolDeserializer()
    {
        var request = new AstQueryRequest(new("tenant", "database", "domain", "partition"),
            new("orders", null, [new("*", "*")], new NullTest(new FieldOperand("/n"), false, false), [], 10));
        var json = System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(request)).Replace("\"kind\":\"nullTest\"", "\"kind\":\"script\"", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonDefaults.Deserialize<AstQueryRequest>(System.Text.Encoding.UTF8.GetBytes(json)));
    }
    [Fact]
    public void InvalidCursorIsRejectedBeforeACollectionScanUsesItsBudget()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(new DatabaseEngine(db.Store, new AuthorizationPolicy(), new() { MaxScanRecords = 1 }));
        var request = KeyLoadQuery<Order>.From(db.Partition, "orders").ToRequest(true, "invalid-cursor");
        Assert.Equal(ErrorCode.CursorExpired, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", request)).Code);
        Assert.Equal(ErrorCode.BudgetExceeded, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", request with { Cursor = null })).Code);
    }
    [Fact]
    public void CatalogAndOtherCollectionWritesPreserveTheCursorButSourceWritesInvalidateIt()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("other", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var engine = new QueryEngine(db.Database); var request = KeyLoadQuery<Order>.From(db.Partition, "orders").Take(1).ToRequest(true);
        var first = engine.ExecuteAst("root", request);
        db.Submit(OperationKind.SetDispatch, true).Get<bool>(); db.Commit(new PutDocument("other", "unrelated", "{}"));
        var continued = engine.ExecuteAst("root", request with { Cursor = first.Cursor });
        Assert.Equal("b", Assert.Single(continued.Rows).EntityId); Assert.Equal(first.CutPosition, continued.CutPosition);
        db.Commit(new PatchDocument("orders", "a", [new("/changed", PatchKind.Set, "true")], 1));
        Assert.Equal(ErrorCode.CursorExpired, Assert.Throws<KeyLoadException>(() => engine.ExecuteAst("root", request with { Cursor = first.Cursor })).Code);
    }
}
