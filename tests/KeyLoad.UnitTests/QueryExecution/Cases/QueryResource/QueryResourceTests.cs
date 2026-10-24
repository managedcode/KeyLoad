using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryResourceTests
{
    private const string Collection = "orders";
    private const string OrderedSql = "SELECT TOP (2) * FROM orders ORDER BY score DESC";
    private const string FullSql = "SELECT TOP (10) * FROM orders ORDER BY score DESC";
    private const string BudgetSql = "SELECT TOP (1) * FROM orders";
    private const string ExplainSql = "SELECT * FROM orders ORDER BY score";
    private const string ExpectedOrder = "s,a,z,b,n,m";

    [Test]
    public async Task AcMp003PagedTopPrefixMatchesFullOrderIncludingTiesAndMixedScalars()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "z", "{\"score\":1}"),
            new PutDocument(Collection, "a", "{\"score\":1}"),
            new PutDocument(Collection, "n", "{\"score\":null}"),
            new PutDocument(Collection, "m", "{}"),
            new PutDocument(Collection, "s", "{\"score\":\"high\"}"),
            new PutDocument(Collection, "b", "{\"score\":true}"));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var expected = engine.Execute("root", new(db.Partition, FullSql, AllowFullScan: true)).Rows
            .Select(row => row.EntityId).ToArray();
        await Assert.That(string.Join(",", expected)).IsEqualTo(ExpectedOrder);
        var actual = new List<string>();
        string? cursor = null;
        do
        {
            var page = engine.Execute("root", new(db.Partition, OrderedSql, AllowFullScan: true, Cursor: cursor));
            actual.AddRange(page.Rows.Select(row => row.EntityId));
            cursor = page.Cursor;
        } while (cursor is not null);
        await Assert.That(string.Join(",", actual)).IsEqualTo(string.Join(",", expected));
    }

    [Test]
    public async Task AcMp003RawCandidateBudgetRejectsBeforeReturningPartialPage()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", "{\"payload\":\"" + new string('x', 16_384) + "\"}"));
        var engine = QueryBudgetReadOwner.Create(db, db.Database.Limits with { MaxQueryReadBytes = 8_192 });
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(db.Partition, BudgetSql, AllowFullScan: true)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(engine.Execute("root", new(db.Partition, "SELECT * FROM orders WHERE id = 'absent'")).Rows).IsEmpty();
    }

    [Test]
    public async Task AcMp003ExplainValidatesAccessPathWithoutEvaluatingOrderAndRejectsOverflow()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", "{\"score\":{\"nested\":1}}"));
        var engine = QueryBudgetReadOwner.Create(db, db.Database.Limits with { MaxScanRecords = 1 });
        var explain = engine.ExecuteAst("root", new(db.Partition,
            new SqlParser(ExplainSql, UnitExecutionOptions.DatabaseLimits(db.Database.Limits),
                UnitExecutionOptions.QueryExecution()).Parse() with
            {
                Explain = true
            }, AllowFullScan: true));
        await Assert.That(explain.AccessPath).IsEqualTo("bounded-full-scan");
        db.Commit(new PutDocument(Collection, "b", "{\"score\":2}"));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(db.Partition, new SqlParser(ExplainSql,
                UnitExecutionOptions.DatabaseLimits(db.Database.Limits),
                UnitExecutionOptions.QueryExecution()).Parse() with
            {
                Explain = true
            }, AllowFullScan: true)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp012CancelledSqlAndLiveCallsReleaseAdmission()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root",
            new(db.Partition, BudgetSql, AllowFullScan: true), cancellationToken: cancelled.Token));
        await Assert.That(engine.Execute("root", new(db.Partition, BudgetSql, AllowFullScan: true)).Rows).IsEmpty();
        var live = new StartLiveQueryRequest(new(db.Partition,
            new SelectQuery(Collection, null, [new("*", "document")], null, [], 1), AllowFullScan: true));
        Assert.ThrowsExactly<OperationCanceledException>(() => engine.StartLiveQuery("root", live,
            cancellationToken: cancelled.Token));
        await Assert.That(engine.StartLiveQuery("root", live).Rows).IsEmpty();
    }
}
