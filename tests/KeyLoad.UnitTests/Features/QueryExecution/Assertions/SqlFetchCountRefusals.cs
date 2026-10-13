using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlFetchCountRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        foreach (var tail in new[] { "FETCH FIRST 0 ROWS ONLY", "FETCH FIRST -1 ROWS ONLY",
            "FETCH FIRST 2147483648 ROWS ONLY", "FETCH FIRST 1.5 ROWS ONLY",
            "FETCH FIRST '2' ROWS ONLY", "FETCH FIRST @count ROWS ONLY", "FETCH FIRST 2 ONLY", "FETCH FIRST 2 ROWS" })
        {
            await ErrorAsync(() => engine.Execute(SqlFetchCountProtocol.Root,
                SqlFetchCountProtocol.Sql(database.Partition, tail)), ErrorCode.Validation);
            await UnchangedAsync(database.Store, image, position, identity);
            await SqlFetchCountPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlFetchCountProtocol.Root);
        }
        foreach (var tail in new[] { "FETCH FIRST 2 ROWS WITH TIES", "LIMIT 2 FETCH FIRST ROW ONLY",
            "FETCH FIRST ROW ONLY LIMIT 2", "FETCH FIRST ROW ONLY FETCH NEXT ROW ONLY" })
        {
            await ErrorAsync(() => engine.Execute(SqlFetchCountProtocol.Root,
                SqlFetchCountProtocol.Sql(database.Partition, tail)), ErrorCode.UnsupportedCapability);
            await UnchangedAsync(database.Store, image, position, identity);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlFetchCountProtocol.Root,
            SqlFetchCountProtocol.Sql(database.Partition, "FETCH FIRST ROW ONLY"), cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await UnchangedAsync(database.Store, image, position, identity);
        await SqlFetchCountPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlFetchCountProtocol.Root);
    }
    internal static async Task DeniedAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await ErrorAsync(() => engine.Execute(SqlFetchCountProtocol.Reader,
            SqlFetchCountProtocol.Sql(database.Partition, "FETCH FIRST ROW ONLY")), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlFetchCountProtocol.Reader,
            SqlFetchCountProtocol.Ast(database.Partition, SqlFetchCountProtocol.ResultCount)), ErrorCode.PermissionDenied);
        await UnchangedAsync(database.Store, image, position, identity);
        await SqlFetchCountPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlFetchCountProtocol.Root);
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
