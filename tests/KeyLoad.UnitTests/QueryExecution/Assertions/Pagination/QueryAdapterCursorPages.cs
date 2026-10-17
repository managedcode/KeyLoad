using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterCursorPages
{
    private const string Principal = "root";
    private const decimal ChangedBoundary = 3m;

    internal static async Task RunAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition)
    {
        var first = await ReadAsync(store, () => engine.Execute(Principal,
            new(partition, QueryAdapterCursorProtocol.Sql, AllowFullScan: true)), "a", continues: true);
        var second = await ReadAsync(store, () => engine.ExecuteAst(Principal,
            QueryAdapterCursorProtocol.Json(partition, first.Cursor)), "b", continues: true);
        var builder = QueryAdapterCursorProtocol.Builder(partition);
        var third = await ReadAsync(store, () => engine.ExecuteAst(Principal,
            QueryAdapterTestSupport.RoundTrip(builder.ToRequest(true, second.Cursor))), "c", continues: false);
        await Assert.That(System.Linq.Enumerable.Single(first.Rows).EntityId).IsEqualTo("a");
        await Assert.That(System.Linq.Enumerable.Single(second.Rows).EntityId).IsEqualTo("b");
        await Assert.That(System.Linq.Enumerable.Single(third.Rows).EntityId).IsEqualTo("c");
        await Assert.That(third.Cursor).IsNull();
        await ReadAsync(store, () => engine.Execute(Principal,
            new(partition, QueryAdapterCursorProtocol.EquivalentSql, AllowFullScan: true)), "a", continues: true);
        await ReadAsync(store, () => engine.ExecuteAst(Principal, QueryAdapterCursorProtocol.Json(partition)), "a", continues: true);
        await ReadAsync(store, () => engine.ExecuteAst(Principal,
            QueryAdapterTestSupport.RoundTrip(builder.ToRequest(true))), "a", continues: true);
        await ChangedQueryAsync(engine, store, partition, first.Cursor);
        await ReadAsync(store, () => engine.Execute(Principal,
            new(partition, QueryAdapterCursorProtocol.EquivalentSql, AllowFullScan: true, Cursor: first.Cursor)), "b", continues: true);
        await ReadAsync(store, () => engine.ExecuteAst(Principal,
            QueryAdapterCursorProtocol.Json(partition, second.Cursor)), "c", continues: false);
    }

    private static async Task ChangedQueryAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, string? cursor)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var changed = QueryAdapterCursorProtocol.Builder(partition).Where(row => row.Number >= ChangedBoundary);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Principal,
                QueryAdapterTestSupport.RoundTrip(changed.ToRequest(true, cursor))));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.CursorExpired);
            await Assert.That(error.Message).IsEqualTo(QueryAdapterCursorProtocol.CursorRefused);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<QueryPage> ReadAsync(ZoneTreeStore store, Func<QueryPage> read, string id, bool continues)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        QueryPage? page = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            page = read();
            await QueryAdapterCursorProtocol.FullPageAsync(page, id, position, continues);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return page ?? throw new InvalidOperationException(MissingPage);
    }
    private const string MissingPage = "The actual original query page is absent.";
}
