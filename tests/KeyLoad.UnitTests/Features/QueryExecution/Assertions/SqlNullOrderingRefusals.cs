using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var good = SqlNullOrderingProtocol.Sql(database.Partition, false, true);
        foreach (var tail in new[] { "NULLS", "NULLS MIDDLE", "NULLS @placement", "NULLS 'FIRST'" })
        {
            await ErrorAsync(() => engine.Execute(SqlNullOrderingProtocol.Root,
                good with { Sql = good.Sql.Replace("NULLS FIRST", tail, StringComparison.Ordinal) }), ErrorCode.Validation);
            await UnchangedAsync(database.Store, image, position, identity);
            await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Root);
        }
        var first = engine.Execute(SqlNullOrderingProtocol.Root, good with { Sql = good.Sql.Replace("LIMIT 5", "LIMIT 2", StringComparison.Ordinal) });
        await Assert.That(first.Cursor).IsNotNull();
        var changed = SqlNullOrderingProtocol.Ast(database.Partition, false, false, 2) with { Cursor = first.Cursor };
        await ErrorAsync(() => engine.ExecuteAst(SqlNullOrderingProtocol.Root, changed), ErrorCode.CursorExpired);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Root, good,
            cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
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
            SqlNullOrderingProtocol.Sql(database.Partition, false, true)), ErrorCode.PermissionDenied);
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
    private static async Task ErrorAsync(Action operation, ErrorCode code)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(code);
}
