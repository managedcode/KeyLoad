using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryAdapterContractTests
{
    [Test]
    public Task CursorContinuesAcrossEquivalentSqlJsonAndCSharpForms()
        => QueryAdapterCursorFlow.RunAsync();

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
        => await QueryAdapterNullParityFlow.RunAsync();

    [Test]
    public async Task UnsupportedExpressionsNeverInvokeApplicationGettersOrDelegates()
        => await QueryAdapterCollectionFlow.RunAsync();

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
        await QueryAdapterBranchFlow.RunAsync();
    }

}
