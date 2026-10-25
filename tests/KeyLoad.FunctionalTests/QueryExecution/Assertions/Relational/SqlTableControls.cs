namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlTableControls
{
    internal static async Task RefusalsAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var sql in new[] { "TABLE",
            "SELECT TOP (0) * FROM offsetleft",
            "SELECT * FROM offsetleft ORDER BY order_id OFFSET -1 ROWS FETCH NEXT 1 ROW ONLY",
            "SELECT TOP (2147483648) * FROM offsetleft",
            "SELECT * FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH FIRST @count ROWS ONLY" })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)), ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlTablePages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        }
        foreach (var sql in new[] { "SELECT DISTINCT * FROM offsetleft",
            "SELECT TOP (1) PERCENT * FROM offsetleft",
            "SELECT COUNT(*) FROM offsetleft",
            "SELECT * FROM offsetleft UNION SELECT * FROM offsetright",
            "SELECT * FROM offsetleft, offsetright",
            "SELECT * FROM offsetleft WITH (NOLOCK)",
            "SELECT order_id FROM offsetleft GROUP BY order_id" })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)), ErrorCode.UnsupportedCapability);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlTablePages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        }
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    internal static async Task DeniedAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, SqlTablePages.TableSql, AllowFullScan: true)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Reader,
            SqlTablePages.Ast(database.Partition)), ErrorCode.PermissionDenied);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    private static async Task ErrorAsync(Action operation, ErrorCode code)
        => await global::TUnit.Assertions.Assert.That(
            global::TUnit.Assertions.Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(code);
}
