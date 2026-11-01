using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryExecutionPlanTests
{
    private const string Collection = "orders";
    private const string Root = "root";
    private const string Index = "number";
    private const string Field = "/number";
    private const string Reader = "reader";
    private const string PrivatePayload = "private-plan-payload";
    private const string PlanSql = "SELECT TOP 2 id FROM orders WHERE number >= 2 AND number <= 4 ORDER BY number DESC";
    private const string ScanProperty = "scan";
    private const string CutProperty = "readCut";
    private const string LimitProperty = "limit";
    private const string RetainedProperty = "retainedRows";
    private const string IndexProperty = "index";
    private const string FieldsProperty = "seekFields";
    private const string LowerProperty = "hasLowerBound";
    private const string UpperProperty = "hasUpperBound";
    private const string OperatorsProperty = "operators";
    private const string OffsetProperty = "offset";

    [Test]
    [Arguments("SELECT TOP 2 id FROM orders WHERE id = 'a'", "point-lookup", "point", false)]
    [Arguments("SELECT TOP 2 id FROM orders WHERE number = 3", "index-prefix-seek", "index:number", false)]
    [Arguments(PlanSql, "index-range-seek", "index:number", false)]
    [Arguments("SELECT TOP 2 id FROM orders ORDER BY number DESC", "bounded-full-scan", "bounded-full-scan", true)]
    public async Task AcQuery004ExplainUsesTheExecutionPlanWithoutVisitingCandidates(
        string sql, string scan, string accessPath, bool allowFullScan)
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = new SqlParser(sql, UnitExecutionOptions.QueryExecution()).Parse();
        var before = db.Store.GetReadDiagnostics();
        var cut = db.Store.Position;
        var explanation = engine.ExecuteAst(Root, new(db.Partition, query with { Explain = true }, AllowFullScan: allowFullScan));
        var after = db.Store.GetReadDiagnostics();
        using var plan = JsonDocument.Parse(explanation.Rows.Single().Json);

        await Assert.That(explanation.AccessPath).IsEqualTo(accessPath);
        await Assert.That(plan.RootElement.GetProperty(ScanProperty).GetString()).IsEqualTo(scan);
        await Assert.That(plan.RootElement.GetProperty(CutProperty).GetInt64()).IsEqualTo(cut);
        await Assert.That(plan.RootElement.GetProperty(LimitProperty).GetInt32()).IsEqualTo(2);
        await Assert.That(plan.RootElement.GetProperty(RetainedProperty).GetInt64()).IsEqualTo(2);
        await Assert.That(explanation.Rows.Single().Json.Contains(PrivatePayload, StringComparison.Ordinal)).IsFalse();
        await Assert.That(after.RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await Assert.That(db.Store.Position).IsEqualTo(cut);
        var executed = engine.ExecuteAst(Root, new(db.Partition, query, AllowFullScan: allowFullScan));
        await Assert.That(executed.AccessPath).IsEqualTo(accessPath);
        await Assert.That(executed.Rows.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task AcQuery004RangePlanDescribesBoundsAndResidualWorkAfterFreshAuthorization()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = new SqlParser(PlanSql, UnitExecutionOptions.QueryExecution()).Parse() with { Explain = true };
        using var plan = JsonDocument.Parse(engine.ExecuteAst(Root, new(db.Partition, query)).Rows.Single().Json);
        var actual = plan.RootElement;

        await Assert.That(actual.GetProperty(IndexProperty).GetString()).IsEqualTo(Index);
        await Assert.That(actual.GetProperty(FieldsProperty)[0].GetString()).IsEqualTo(Field);
        await Assert.That(actual.GetProperty(LowerProperty).GetBoolean()).IsTrue();
        await Assert.That(actual.GetProperty(UpperProperty).GetBoolean()).IsTrue();
        await Assert.That(string.Join(',', actual.GetProperty(OperatorsProperty).EnumerateArray().Select(item => item.GetString())))
            .IsEqualTo("index-range-seek,row-authorization,residual-filter,top-n-heap,projection,page");
        _ = db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(new(Reader, "tenant", [], []))).Get<PrincipalRecord>();
        var before = db.Store.GetReadDiagnostics();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Reader, new(db.Partition, query)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await Assert.That(engine.ExecuteAst(Root, new(db.Partition, query)).Rows).HasSingleItem();
    }

    [Test]
    public async Task AcQuery004ContinuationPlanUsesTheResolvedPageOffset()
    {
        using var db = Create();
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = new SqlParser("SELECT TOP 1 id FROM orders WHERE number >= 2 ORDER BY number",
            UnitExecutionOptions.QueryExecution()).Parse();
        var first = engine.ExecuteAst(Root, new(db.Partition, query));
        await Assert.That(first.Cursor).IsNotNull();
        var request = new AstQueryRequest(db.Partition, query with { Explain = true }, Cursor: first.Cursor);
        var before = db.Store.GetReadDiagnostics();
        using var plan = JsonDocument.Parse(engine.ExecuteAst(Root, request).Rows.Single().Json);
        await Assert.That(plan.RootElement.GetProperty(OffsetProperty).GetInt32()).IsEqualTo(1);
        await Assert.That(plan.RootElement.GetProperty(RetainedProperty).GetInt64()).IsEqualTo(2);
        await Assert.That(db.Store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await Assert.That(engine.ExecuteAst(Root, request with { Query = query }).Rows.Single().EntityId).IsEqualTo("b");
    }

    private static TestDatabase Create()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, [Field])]);
        db.Commit(new PutDocument(Collection, "a", "{\"number\":2,\"body\":\"private-plan-payload\"}"),
            new PutDocument(Collection, "b", "{\"number\":3}"), new PutDocument(Collection, "c", "{\"number\":4}"));
        return db;
    }
}
