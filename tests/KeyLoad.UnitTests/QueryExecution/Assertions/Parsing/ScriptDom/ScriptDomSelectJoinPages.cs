namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomSelectJoinPages
{
    internal static async Task RequireAsync(global::KeyLoad.Query.QueryEngine engine, PartitionRef partition, long position)
    {
        var query = new QueryRequest(partition, Sql, AllowFullScan: true, QueryDialectVersion: JoinVersion);
        var ast = new global::KeyLoad.Query.AstQueryRequest(partition,
            new(SqlOffsetProtocol.Left, Left, [new("/title", "title", Left), new("/name", "name", Right)],
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand("/order_id"), "<",
                    global::KeyLoad.Query.ValueOperand.Create("c")),
                [new("/order_id", false)], Limit, InnerJoin: new(SqlOffsetProtocol.Right, Right, "/customer_id", "/id"),
                Offset: Offset), AllowFullScan: true, AstVersion: JoinVersion);
        var expected = new QueryPage(
            [new("b", Revision, """{"title":"second","name":"Bee"}""", false, [],
                [new(Left, "b", Revision), new(Right, "c2", Revision)])],
            null, position, AccessPath);
        await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Reader, query), expected);
        await SqlOffsetPages.ExactAsync(engine.ExecuteAst(SqlOffsetProtocol.Reader, ast), expected);
    }
    private const string Sql = "SELECT l.title AS title, r.name AS name FROM offsetleft AS l INNER JOIN offsetright AS r ON l.customer_id = r.id WHERE l.order_id < 'c' ORDER BY l.order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    private const string Left = "l";
    private const string Right = "r";
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const int JoinVersion = SqlOperationProtocol.DefaultQueryDialectVersion;
    private const int Limit = 1;
    private const int Offset = 1;
    private const long Revision = 1;
}
