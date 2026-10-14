namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomSelectPredicatePages
{
    internal static async Task RequireAsync(global::KeyLoad.Query.QueryEngine engine, PartitionRef partition, long position)
    {
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            [Lower] = global::System.Text.Json.JsonSerializer.SerializeToElement("a", JsonDefaults.Options),
            [Upper] = global::System.Text.Json.JsonSerializer.SerializeToElement("b", JsonDefaults.Options)
        };
        var page = engine.Execute(SqlOffsetProtocol.Reader, new(partition, Sql, parameters, AllowFullScan: true));
        var expected = new QueryPage(
            [new("a", Revision, """{"title":"first","order_id":"a"}""", false, []),
             new("b", Revision, """{"title":"second","order_id":"b"}""", false, [])],
            null, position, ScriptDomSelectPages.FullScan);
        await SqlOffsetPages.ExactAsync(page, expected);
        var predicate = new global::KeyLoad.Query.Logical(
            new global::KeyLoad.Query.InPredicate(new global::KeyLoad.Query.FieldOperand("/order_id"),
                [new global::KeyLoad.Query.ParameterOperand(Lower), new global::KeyLoad.Query.ParameterOperand(Upper)], false),
            "AND", new global::KeyLoad.Query.Logical(
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand("/order_id"), ">=", global::KeyLoad.Query.ValueOperand.Create("a")),
                "AND", new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand("/order_id"), "<=", global::KeyLoad.Query.ValueOperand.Create("c"))));
        var ast = new global::KeyLoad.Query.AstQueryRequest(partition,
            new(SqlOffsetProtocol.Left, null, [new("/title", "title"), new("/order_id", "order_id")], predicate,
                [new("/order_id", false)], Limit), parameters, AllowFullScan: true);
        await SqlOffsetPages.ExactAsync(engine.ExecuteAst(SqlOffsetProtocol.Reader, ast), expected);
    }
    private const string Sql = "SELECT TOP (2) title, order_id FROM offsetleft WHERE order_id IN (@lower, @upper) AND order_id BETWEEN 'a' AND 'c' ORDER BY order_id";
    private const string Lower = "lower";
    private const string Upper = "upper";
    private const int Limit = 2;
    private const long Revision = 1;
}
