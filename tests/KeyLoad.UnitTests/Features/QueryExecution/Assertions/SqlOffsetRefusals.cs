using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOffsetRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlOffsetProtocol.JoinRequest(database.Partition);
        foreach (var (tail, code) in new[]
        {
            ("LIMIT 1 OFFSET -1", ErrorCode.Validation),
            ("LIMIT 1 OFFSET 2147483647", ErrorCode.Validation),
            ("LIMIT 1 OFFSET 1 OFFSET 1", ErrorCode.UnsupportedCapability),
            ("LIMIT 1 OFFSET @skip", ErrorCode.Validation),
            ("OFFSET -1 ROWS FETCH FIRST ROW ONLY", ErrorCode.Validation),
            ("OFFSET 2147483647 ROWS FETCH NEXT ROW ONLY", ErrorCode.Validation),
            ("OFFSET 1 ROWS FETCH NEXT 0 ROWS ONLY", ErrorCode.Validation),
            ("OFFSET 1 ROWS FETCH FIRST ROW ONLY FETCH NEXT ROW ONLY", ErrorCode.UnsupportedCapability),
            ("OFFSET 1 ROWS FETCH NEXT ROW WITH TIES", ErrorCode.UnsupportedCapability)
        })
        {
            var bad = request with { Sql = request.Sql.Replace("LIMIT 1 OFFSET 1", tail, StringComparison.Ordinal) };
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Root, bad), code);
            await UnchangedAsync(database.Store, before, position, identity);
        }
        var ast = SqlOffsetProtocol.Join(database.Partition);
        foreach (var offset in new[] { -1, SqlOffsetProtocol.WindowBound, int.MaxValue })
        {
            await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Root,
                ast with { Query = ast.Query with { Offset = offset } }), ErrorCode.BudgetExceeded);
            await UnchangedAsync(database.Store, before, position, identity);
        }
        var first = engine.Execute(SqlOffsetProtocol.Root, SqlOffsetProtocol.ScalarRequest(database.Partition));
        var different = SqlOffsetProtocol.Scalar(database.Partition);
        await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Root, different with
        { Cursor = first.Cursor, Query = different.Query with { Offset = default } }), ErrorCode.CursorExpired);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.ExecuteAst(SqlOffsetProtocol.Root,
            ast, cancellationToken: cancelled.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
        await UnchangedAsync(database.Store, before, position, identity);
    }
    internal static async Task DeniedAsync(TestDatabase database)
    {
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader, SqlOffsetProtocol.JoinRequest(database.Partition)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Reader, SqlOffsetProtocol.Join(database.Partition)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Reader, SqlOffsetProtocol.ScalarRequest(database.Partition)), ErrorCode.PermissionDenied);
        await ErrorAsync(() => engine.ExecuteAst(SqlOffsetProtocol.Reader, SqlOffsetProtocol.Scalar(database.Partition)), ErrorCode.PermissionDenied);
        await UnchangedAsync(database.Store, before, position, identity);
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
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(code);
    }
}
