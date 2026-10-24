using KeyLoad.Query;
namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNullOrderingRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var good = SqlNullOrderingProtocol.Sql(database.Partition, false, true);
        await SqlAllPages.InvalidAsync(database, engine);
        await SqlImplicitAliasPages.InvalidAsync(database, engine);
        await UsingInvalidAsync(database, engine);
        foreach (var tail in new[] { "NULLS", "NULLS MIDDLE", "NULLS @placement", "NULLS 'FIRST'" })
        {
            await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Root,
                good with
                {
                    Sql = good.Sql.Replace("NULLS FIRST", tail, StringComparison.Ordinal)
                }), ErrorCode.Validation);
            await UnchangedAsync(database.Store, image, position, identity);
            await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Root);
        }
        var first = engine.ExecuteAst(SqlNullOrderingProtocol.Root, SqlNullOrderingProtocol.Ast(database.Partition, false, true, 2));
        await Assert.That(first.Cursor).IsNotNull();
        var changed = SqlNullOrderingProtocol.Ast(database.Partition, false, false, 2) with
        {
            Cursor = first.Cursor
        };
        await ErrorAsync(() => engine.ExecuteAst(SqlNullOrderingProtocol.Root, changed), ErrorCode.CursorExpired);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        foreach (var request in new[] { SqlNullOrderingProtocol.Execution(database.Partition, false),
            SqlNullOrderingProtocol.Execution(database.Partition, true) })
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Root, request,
                cancellationToken: cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        }
        await UnchangedAsync(database.Store, image, position, identity);
        await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Root);
    }

    internal static async Task DeniedAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            SqlNullOrderingProtocol.Execution(database.Partition, false)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            SqlNullOrderingProtocol.Execution(database.Partition, true)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlAllPages.DuplicateSql, AllowFullScan: true)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlImplicitAliasPages.BasicSql, AllowFullScan: true)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
            SqlNullOrderingProtocol.Ast(database.Partition, true, false)), ErrorCode.PermissionDenied);
        await UnchangedAsync(database.Store, image, position, identity);
        await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Root);
    }

    internal static async Task UnchangedAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        string[] image, long position, byte[] identity)
    {
        await Assert.That(SqlInnerJoinRetainedIdentityFlow.Image(store))
            .IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
    }
    private static async Task UsingInvalidAsync(TestDatabase database, QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cut = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var good = SqlNullOrderingProtocol.Execution(database.Partition, false);
        foreach (var (direction, code) in new[]
        {
            (" USING ", ErrorCode.Validation), (" USING '>' ", ErrorCode.Validation),
            (" USING @direction ", ErrorCode.Validation), (" USING = ", ErrorCode.Validation),
            (" USING <= ", ErrorCode.Validation), (" ASC USING < ", ErrorCode.Validation),
            (" USING < DESC ", ErrorCode.Validation)
        })
        {
            await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Root,
                good with
                {
                    Sql = good.Sql.Replace(SqlNullOrderingProtocol.RuntimeAscending, direction, StringComparison.Ordinal)
                }), code);
            await UnchangedAsync(database.Store, image, cut, identity);
            await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store,
                database.Partition, SqlNullOrderingProtocol.Root);
            await UnchangedAsync(database.Store, image, cut, identity);
        }
    }

    private static async Task ErrorAsync(Action operation, ErrorCode code)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(code);
}
