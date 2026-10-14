namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomMoneyModePages
{
    internal static async Task RequireAsync(TestDatabase database)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var named = new QueryRequest(database.Partition, NamedSql,
            new(StringComparer.Ordinal) { [NamedParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement("a", JsonDefaults.Options) },
            AllowFullScan: true);
        var expected = new QueryPage([new("a", Revision, """{"title":"first"}""", false, [])],
            null, position, ScriptDomSelectPages.FullScan);
        await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Reader, named), expected);
        var wire = named with
        {
            Sql = SlotSql,
            Parameters = new(StringComparer.Ordinal)
            { [SlotParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement("a", JsonDefaults.Options) }
        };
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(database.Database.OperationLimitsOptions);
        var statement = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(wire.Sql,
            database.Database.OperationLimitsOptions, budget.Check, CancellationToken.None);
        global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.ValidateNativeSessionSlots(statement,
            SingleSlot, budget.Check, CancellationToken.None);
        var ast = global::KeyLoad.Query.Features.QueryExecution.ScriptDomQueryAdapter.Lower(statement, wire,
            database.Database.OperationLimitsOptions, UnitExecutionOptions.QueryExecution(), budget, SingleSlot, CancellationToken.None);
        await SqlOffsetPages.ExactAsync(engine.ExecuteAst(SqlOffsetProtocol.Reader, ast), expected);
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader, wire));
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        foreach (var slot in InvalidSlots)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => Validate(database, slot));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Reader, named), expected);
        }
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    private static void Validate(TestDatabase database, string slot)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(database.Database.OperationLimitsOptions);
        var statement = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(
            Prefix + slot + Suffix, database.Database.OperationLimitsOptions, budget.Check, CancellationToken.None);
        global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.ValidateNativeSessionSlots(statement,
            SingleSlot, budget.Check, CancellationToken.None);
    }
    private const string Prefix = "SELECT TOP (1) title FROM offsetleft WHERE order_id = ";
    private const string Suffix = " ORDER BY order_id";
    private const string NamedSql = Prefix + "@wanted" + Suffix;
    private const string SlotSql = Prefix + "$01" + Suffix;
    private const string NamedParameter = "wanted";
    private const string SlotParameter = "p1";
    private const int SingleSlot = 1;
    private const long Revision = 1;
    private static readonly string[] InvalidSlots = ["$0", "$2", "$2147483648", "$1.5", "@wanted"];
}
