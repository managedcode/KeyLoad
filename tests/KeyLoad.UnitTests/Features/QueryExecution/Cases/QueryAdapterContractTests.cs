using System.Linq.Expressions;
using KeyLoad.Client;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryAdapterContractTests
{
    [Test]
    public async Task CursorContinuesAcrossEquivalentSqlJsonAndCSharpForms()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"number\":2}"), new PutDocument("orders", "b", "{\"number\":3}"), new PutDocument("orders", "c", "{\"number\":4}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        const string sql = "SELECT d.id, d.number FROM orders d WHERE d.number >= 2.00 ORDER BY d.id LIMIT 1";
        var first = engine.Execute("root", new(db.Partition, sql, AllowFullScan: true));
        await Assert.That(System.Linq.Enumerable.Single(first.Rows).EntityId).IsEqualTo("a");
        var json = QueryAdapterTestSupport.RoundTrip(new(db.Partition, new SqlParser(sql.Replace("2.00", "2", StringComparison.Ordinal), new()).Parse(),
            new(), true, first.Cursor));
        var second = engine.ExecuteAst("root", json);
        await Assert.That(System.Linq.Enumerable.Single(second.Rows).EntityId).IsEqualTo("b");
        var builder = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).Where(row => row.Number >= 2m)
            .OrderBy(row => QueryFunctions.DocumentId(row)).Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number }).Take(1);
        var third = engine.ExecuteAst("root", QueryAdapterTestSupport.RoundTrip(builder.ToRequest(true, second.Cursor)));
        await Assert.That(System.Linq.Enumerable.Single(third.Rows).EntityId).IsEqualTo("c");
        await Assert.That(third.Cursor).IsNull();
    }

    [Test]
    public async Task EveryAdapterChecksSensitivePredicateAndOrderingAndProjectsTheSameOmission()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/odd.name", "pii")]);
        db.Commit(new PutDocument("orders", "a", "{\"odd.name\":\"CANARY\",\"number\":1,\"status\":\"open\"}"));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.Query | Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var filter = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).Where(row => row.Secret == "CANARY").ToRequest(true);
        var sort = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).OrderBy(row => row.Secret).ToRequest(true);
        foreach (var query in new[] { filter, sort })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("reader", QueryAdapterTestSupport.RoundTrip(query))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        var safe = engine.ExecuteAst("reader", QueryAdapterTestSupport.RoundTrip(KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).ToRequest(true)));
        var sql = engine.Execute("reader", new(db.Partition, "SELECT * FROM orders", AllowFullScan: true));
        await QueryAdapterTestSupport.SameRows(sql, safe);
        await Assert.That(System.Linq.Enumerable.Single(safe.Rows).Json).DoesNotContain("CANARY");
    }

    [Test]
    public async Task NullMissingAndInHaveExplicitCanonicalSemantics()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "null", "{\"n\":null,\"status\":\"open\"}"), new PutDocument("orders", "missing", "{\"status\":\"hold\"}"),
            new PutDocument("orders", "value", "{\"n\":2,\"status\":\"closed\"}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var set = new[] { "open", "hold" };
        Expression<Func<QueryAdapterOrder, bool>> filter = row => set.Contains(row.Status)
            && (QueryFunctions.IsMissing(row.Optional) || QueryFunctions.IsNull(row.Optional));
        var query = KeyLoadQuery.From<QueryAdapterOrder>(db.Partition, "orders", UnitClientOptions.Translation()).Where(filter).OrderBy(row => QueryFunctions.DocumentId(row));
        var actual = engine.ExecuteAst("root", QueryAdapterTestSupport.RoundTrip(query.ToRequest(true)));
        var sql = engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE status IN ('open','hold') AND (n IS MISSING OR n IS NULL) ORDER BY id", AllowFullScan: true));
        await QueryAdapterTestSupport.SameRows(sql, actual);
        await Assert.That(actual.Rows.Select(row => row.EntityId)).IsEquivalentTo(new[] { "missing", "null" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsupportedExpressionsNeverInvokeApplicationGettersOrDelegates()
    {
        var query = KeyLoadQuery.From<QueryAdapterOrder>(new("tenant", "database", "domain", QueryAdapterTestTokens.Partition), "orders", UnitClientOptions.Translation());
        var constant = new QueryAdapterUnsafeConstant();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => query.Where(row => row.Status == constant.Value)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(constant.Calls).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => query.Where(row => row.Status.StartsWith('o'))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => query.Where(row => (int)row.Number > 2)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task BuilderBranchesHaveIndependentQueryContexts()
    {
        var original = KeyLoadQuery.From<QueryAdapterOrder>(new("tenant", "database", "domain", QueryAdapterTestTokens.Partition), "orders", UnitClientOptions.Translation());
        var a = original.Where(row => row.Number > 3m).OrderBy(row => row.Number).Take(1);
        var b = original.Where(row => row.Status == "open").OrderByDescending(row => row.Status).Take(2);
        await Assert.That(original.ToRequest().Query.Filter).IsNull();
        await Assert.That(original.ToRequest().Query.Order).IsEmpty();
        await Assert.That(original.ToRequest().Query.Limit).IsEqualTo(100);
        await Assert.That(a.ToRequest().Query.Limit).IsEqualTo(1);
        await Assert.That(b.ToRequest().Query.Limit).IsEqualTo(2);
        await Assert.That(System.Linq.Enumerable.Single(a.ToRequest().Query.Order).Path).IsEqualTo("/number");
        await Assert.That(System.Linq.Enumerable.Single(b.ToRequest().Query.Order).Path).IsEqualTo("/status");
    }

}
