namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlTablePages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var sql in new[] { TableSql, FetchSql, SelectSql })
        {
            var request = new QueryRequest(partition, sql, AllowFullScan: true);
            var page = engine.Execute(principal, request);
            await PageAsync(page, SecondId, SecondJson, position, final: false);
            page = engine.Execute(principal, request with { Cursor = page.Cursor });
            await PageAsync(page, ThirdId, ThirdJson, position, final: false);
            page = engine.Execute(principal, request with { Cursor = page.Cursor });
            await PageAsync(page, FourthId, FourthJson, position, final: true);
        }
        var ast = Ast(partition);
        var astPage = engine.ExecuteAst(principal, ast);
        await PageAsync(astPage, SecondId, SecondJson, position, final: false);
        astPage = engine.ExecuteAst(principal, ast with { Cursor = astPage.Cursor });
        await PageAsync(astPage, ThirdId, ThirdJson, position, final: false);
        astPage = engine.ExecuteAst(principal, ast with { Cursor = astPage.Cursor });
        await PageAsync(astPage, FourthId, FourthJson, position, final: true);
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
    }

    internal static global::KeyLoad.Query.AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new(SqlOffsetProtocol.Left, null, [new(Star, Star)], null,
            [new(SqlOffsetProtocol.OrderPath, false)], SqlOffsetProtocol.PageSize,
            Offset: SqlOffsetProtocol.InitialOffset), AllowFullScan: true);

    private static async Task PageAsync(QueryPage page, string id, string json, long cut, bool final)
    {
        if (final)
        { await Assert.That(page.Cursor).IsNull(); }
        else
        { await Assert.That(page.Cursor).IsNotNull(); }
        await SqlOffsetPages.ExactAsync(page, new([new(id, SqlOffsetProtocol.InitialRevision, json, false, [])],
            page.Cursor, cut, FullScan));
    }

    internal const string TableSql = "SELECT * FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH FIRST 1 ROW ONLY";
    internal const string FetchSql = "SELECT * FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    internal const string SelectSql = "SELECT ALL * FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    private const string Star = "*";
    private const string FullScan = "bounded-full-scan";
    private const string SecondId = "b";
    private const string ThirdId = "c";
    private const string FourthId = "d";
    private const string SecondJson = "{\"order_id\":\"b\",\"customer_id\":\"c2\",\"title\":\"second\"}";
    private const string ThirdJson = "{\"order_id\":\"c\",\"customer_id\":\"c3\",\"title\":\"third\"}";
    private const string FourthJson = "{\"order_id\":\"d\",\"customer_id\":\"c4\",\"title\":\"fourth\"}";
}
