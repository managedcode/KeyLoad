namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomSelectRefusals
{
    internal static async Task RunAsync(TestDatabase database)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        foreach (var (sql, code) in Refusals)
        {
            var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
            var position = database.Store.Position;
            var identity = JsonDefaults.Serialize(database.Store.Identity);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader,
                new(database.Partition, sql, AllowFullScan: true)));
            await Assert.That(failure.Code).IsEqualTo(code);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
        await ScriptDomSelectCancellation.RequireAsync(database, engine);
        await ScriptDomParsingCancellation.RequireAsync(database, engine);
    }
    internal static async Task DeniedAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, ScriptDomSelectPages.WindowSql, AllowFullScan: true)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    private static readonly (string Sql, ErrorCode Code)[] Refusals =
        [("SELECT TOP (1) FROM offsetleft", ErrorCode.Validation),
         ("SELECT title FROM offsetleft WHERE order_id =", ErrorCode.Validation),
         ("SELECT title FROM offsetleft LIMIT 1", ErrorCode.Validation),
         ("SELECT TOP (1) PERCENT title FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT DISTINCT title FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT COUNT(*) FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT title INTO copied FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT title COLLATE Latin1_General_CI_AS FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft WITH (NOLOCK)", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft; DELETE FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("SELECT TOP (1) title FROM offsetleft\nGO", ErrorCode.UnsupportedCapability),
         ("SELECT TOP (1) title FROM offsetleft\nGO 2", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft GROUP BY title", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft UNION SELECT title FROM offsetleft", ErrorCode.UnsupportedCapability),
         ("WITH c AS (SELECT title FROM offsetleft) SELECT title FROM c", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft WHERE title LIKE 'a%'", ErrorCode.UnsupportedCapability),
         ("SELECT title FROM offsetleft WHERE title IN (SELECT title FROM offsetleft)", ErrorCode.UnsupportedCapability),
         ("SELECT CASE WHEN title IS NULL THEN 1 ELSE 0 END AS flag FROM offsetleft", ErrorCode.UnsupportedCapability)];
}
