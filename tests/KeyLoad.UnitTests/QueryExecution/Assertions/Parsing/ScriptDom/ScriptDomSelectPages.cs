namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomSelectPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var sql = new QueryRequest(partition, WindowSql, AllowFullScan: true);
        var ast = new global::KeyLoad.Query.AstQueryRequest(partition,
            new(SqlOffsetProtocol.Left, null, [new("/title", "title"), new("/order_id", "order_id")], null,
                [new("/order_id", false)], PageSize, Offset: InitialOffset), AllowFullScan: true);
        foreach (var (id, json, final) in Rows)
        {
            var actual = engine.Execute(SqlOffsetProtocol.Reader, sql);
            var expected = engine.ExecuteAst(SqlOffsetProtocol.Reader, ast);
            await PageAsync(actual, id, json, position, final);
            await PageAsync(expected, id, json, position, final);
            sql = sql with
            {
                Cursor = actual.Cursor
            };
            ast = ast with
            {
                Cursor = expected.Cursor
            };
        }
        await ScriptDomSelectPredicatePages.RequireAsync(engine, partition, position);
        await ScriptDomSelectJoinPages.RequireAsync(engine, partition, position);
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
    }
    internal static async Task PageAsync(QueryPage page, string id, string json, long position, bool final)
    {
        if (final)
        {
            await Assert.That(page.Cursor).IsNull();
        }
        else
        {
            await Assert.That(page.Cursor).IsNotNull();
        }
        await SqlOffsetPages.ExactAsync(page, new([new(id, InitialRevision, json, false, [])],
            page.Cursor, position, FullScan));
    }
    internal const string WindowSql = "SELECT title, order_id FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    internal const string FullScan = "bounded-full-scan";
    private const int PageSize = 1;
    private const int InitialOffset = 1;
    private const long InitialRevision = 1;
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"second","order_id":"b"}""", false),
         ("c", """{"title":"third","order_id":"c"}""", false),
         ("d", """{"title":"fourth","order_id":"d"}""", true)];
}
