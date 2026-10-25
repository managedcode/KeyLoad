namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class ScriptDomMoneyModePages
{
    internal static async Task RequireAsync(TestDatabase database)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var named = new QueryRequest(database.Partition, NamedSql,
            new(StringComparer.Ordinal)
            {
                [NamedParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement("a", JsonDefaults.Options)
            },
            AllowFullScan: true);
        var expected = new QueryPage([new("a", Revision, """{"title":"first"}""", false, [])],
            null, position, ScriptDomSelectPages.FullScan);
        await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Reader, named), expected);
        var wire = named with
        {
            Sql = SlotSql,
            Parameters = new(StringComparer.Ordinal)
            {
                [SlotParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement("a", JsonDefaults.Options)
            }
        };
        var budget = new global::KeyLoad.Core.ReadExecutionBudget();
        var statement = global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(wire.Sql,
            budget.Check, CancellationToken.None);
        global::KeyLoad.QueryExecution.ScriptDomSyntax.ValidateSessionSlots(statement,
            1, budget.Check, CancellationToken.None);
        var ast = global::KeyLoad.Query.QueryExecution.ScriptDomQueryAdapter.Lower(statement, wire,
            UnitExecutionOptions.QueryExecution(), budget, 1, CancellationToken.None);
        await SqlOffsetPages.ExactAsync(engine.ExecuteAst(SqlOffsetProtocol.Reader, ast), expected);
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader, wire));
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        foreach (var slot in InvalidSlots)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => Validate(slot));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Reader, named), expected);
        }
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    private static void Validate(string slot)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget();
        var statement = global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(
            Prefix + slot + Suffix, budget.Check, CancellationToken.None);
        global::KeyLoad.QueryExecution.ScriptDomSyntax.ValidateSessionSlots(statement,
            1, budget.Check, CancellationToken.None);
    }
    private const string Prefix = "SELECT TOP (1) title FROM offsetleft WHERE order_id = ";
    private const string Suffix = " ORDER BY order_id";
    private const string NamedSql = Prefix + "@wanted" + Suffix;
    private const string SlotSql = Prefix + "$01" + Suffix;
    private const string NamedParameter = "wanted";
    private const string SlotParameter = "p1";
    private const long Revision = 1;
    private static readonly string[] InvalidSlots = ["$0", "$2", "$2147483648", "$1.5", "@wanted"];
}
