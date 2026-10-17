namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOrdinalPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var descending in new[] { false, true })
        {
            var expected = descending ? Reverse : Forward;
            foreach (var ordinal in new[] { "2", "02" })
            {
                var sql = new QueryRequest(partition, Sql(descending, ordinal), AllowFullScan: true);
                var ast = Ast(partition, descending);
                foreach (var (id, json, final) in expected)
                {
                    var sqlPage = engine.Execute(principal, sql);
                    var astPage = engine.ExecuteAst(principal, ast);
                    await PageAsync(sqlPage, id, json, position, final);
                    await PageAsync(astPage, id, json, position, final);
                    sql = sql with
                    {
                        Cursor = sqlPage.Cursor
                    };
                    ast = ast with
                    {
                        Cursor = astPage.Cursor
                    };
                }
            }
        }
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
    }
    internal static string Sql(bool descending, string ordinal = "2")
        => "SELECT title, order_id FROM offsetleft ORDER BY " + ordinal + (descending ? " DESC" : " ASC")
            + " OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    internal static global::KeyLoad.Query.AstQueryRequest Ast(PartitionRef partition, bool descending)
        => new(partition, new(SqlOffsetProtocol.Left, null,
            [new("/title", "title"), new(SqlOffsetProtocol.OrderPath, "order_id")], null,
            [new(SqlOffsetProtocol.OrderPath, descending)], SqlOffsetProtocol.PageSize,
            Offset: SqlOffsetProtocol.InitialOffset), AllowFullScan: true);
    private static async Task PageAsync(QueryPage page, string id, string json, long cut, bool final)
    {
        if (final)
        {
            await Assert.That(page.Cursor).IsNull();
        }
        else
        {
            await Assert.That(page.Cursor).IsNotNull();
        }
        await SqlOffsetPages.ExactAsync(page, new([new(id, SqlOffsetProtocol.InitialRevision, json, false, [])],
            page.Cursor, cut, FullScan));
    }
    internal static readonly (string Id, string Json, bool Final)[] Forward =
        [("b", """{"title":"second","order_id":"b"}""", false),
         ("c", """{"title":"third","order_id":"c"}""", false),
         ("d", """{"title":"fourth","order_id":"d"}""", true)];
    internal static readonly (string Id, string Json, bool Final)[] Reverse =
        [("c", """{"title":"third","order_id":"c"}""", false),
         ("b", """{"title":"second","order_id":"b"}""", false),
         ("a", """{"title":"first","order_id":"a"}""", true)];
    private const string FullScan = "bounded-full-scan";
}
