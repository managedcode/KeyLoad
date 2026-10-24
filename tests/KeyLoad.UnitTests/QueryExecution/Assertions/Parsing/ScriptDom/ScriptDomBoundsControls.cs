namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomBoundsControls
{
    internal static async Task RequireAsync(TestDatabase database)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var nodeProjection = string.Join(Separator, Enumerable.Range(FirstIndex, NodeColumns)
            .Select(index => "title AS t" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
        var inputs = new[]
        {
            Prefix + new string(Oversize, ScriptDomBoundsWholeFlowTests.ByteBound),
            Prefix + string.Join(Conjunction, Enumerable.Repeat(Condition, ExcessTokenConditions)),
            Prefix + new string(Open, ExcessDepth) + Condition + new string(Close, ExcessDepth),
            "SELECT TOP (1) " + nodeProjection + " FROM offsetleft ORDER BY order_id"
        };
        foreach (var sql in inputs)
        {
            var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
            var position = database.Store.Position;
            var identity = JsonDefaults.Serialize(database.Store.Identity);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader,
                new(database.Partition, sql, AllowFullScan: true)));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }
    private const string Prefix = "SELECT TOP (1) title FROM offsetleft WHERE ";
    private const string Condition = "order_id = 'a'";
    private const string Conjunction = " OR ";
    private const string Separator = ", ";
    private const char Oversize = 'x';
    private const char Open = '(';
    private const char Close = ')';
    private const int FirstIndex = 0;
    private const int NodeColumns = 25;
    private const int ExcessTokenConditions = 40;
    private const int ExcessDepth = ScriptDomBoundsWholeFlowTests.DepthBound + 1;
}
