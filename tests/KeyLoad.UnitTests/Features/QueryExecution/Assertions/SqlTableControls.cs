namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlTableControls
{
    internal static async Task RefusalsAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var sql in new[] { "TABLE", "TABLE offsetleft LIMIT 0", "TABLE offsetleft OFFSET -1 LIMIT 1",
            "TABLE offsetleft LIMIT 2147483648", "TABLE offsetleft FETCH FIRST @count ROWS ONLY" })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)), ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlTablePages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        }
        foreach (var sql in new[] { "TABLE offsetleft WHERE order_id = 'b'", "TABLE offsetleft AS l",
            "TABLE offsetleft JOIN offsetright ON customer_id = id", "TABLE offsetleft UNION TABLE offsetright",
            "TABLE offsetleft, offsetright", "TABLE offsetleft FOR UPDATE", "TABLE offsetleft GROUP BY order_id" })
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
