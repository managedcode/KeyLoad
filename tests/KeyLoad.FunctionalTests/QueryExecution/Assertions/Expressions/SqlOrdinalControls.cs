namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlOrdinalControls
{
    internal static async Task RefusalsAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var ordinal in new[] { "0", "3", "2147483648" })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
                new(database.Partition, SqlOrdinalPages.Sql(false, ordinal), AllowFullScan: true)), ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlOrdinalPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        }
        foreach (var ordinal in new[] { "-1", "1.0", "1e0" })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
                new(database.Partition, SqlOrdinalPages.Sql(false, ordinal), AllowFullScan: true)), ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlOrdinalPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        }
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, "SELECT TOP (1) * FROM offsetleft ORDER BY 1", AllowFullScan: true)),
            ErrorCode.UnsupportedCapability);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    internal static async Task DeniedAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, SqlOrdinalPages.Sql(false), AllowFullScan: true)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Reader,
            SqlOrdinalPages.Ast(database.Partition, false)), ErrorCode.PermissionDenied);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    internal static async Task CancelledAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        CancellationToken originalToken)
    {
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, SqlOrdinalPages.Sql(false), AllowFullScan: true), cancellationToken: originalToken));
        await Assert.That(failure.CancellationToken).IsEqualTo(originalToken);
    }
    private static async Task ErrorAsync(Action operation, ErrorCode code)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(code);
}
