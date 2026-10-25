using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.ChangeFeeds;

internal static class DocumentLiveProfileClusterPages
{
    internal static async Task<string> SnapshotAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state, int phase, CancellationToken token)
    {
        var cursor = string.Empty;
        for (var route = 0; route < 4; route++)
        {
            var before = await DocumentLiveProfileClusterValues.CutAsync(reader, state, phase, token);
            var page = await DocumentLiveProfileClusterCalls.StartAsync(reader, state, state.Query, route, token);
            await LiveQueryCutClusterAssertions.BoundsAsync(before, page.CutPosition, await DocumentLiveProfileClusterValues.CutAsync(reader, state, phase, token));
            await Assert.That(page.Cursor).IsNotEmpty();
            await SqlClusterProtocol.EqualAsync(new LiveQuerySnapshot([.. DocumentLiveProfileClusterState.Rows(phase)], page.Cursor, phase == 0 ? 2L : phase == 1 ? 5L : 6L, page.CutPosition), page);
            cursor = page.Cursor;
        }
        return cursor;
    }
    internal static async Task TailAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state, CancellationToken token)
    {
        var receipt = state.Commands[1].Receipt;
        LiveQueryChange[] changes =
        [new(3L, receipt.Token, LiveQueryChangeKind.Upsert, state.StoredReference("a"), 2L, DocumentLiveProfileClusterState.Row("a", 2L, 2)),
         new(4L, receipt.Token, LiveQueryChangeKind.Upsert, state.StoredReference("b"), 1L, DocumentLiveProfileClusterState.Row("b", 1L, 3)),
         new(5L, receipt.Token, LiveQueryChangeKind.Remove, state.StoredReference("a"), 3L, null)];
        for (var route = 0; route < 4; route++)
        {
            var cursor = state.OriginalCursor;
            for (var index = 0; index < changes.Length; index++)
            {
                var request = new ReadLiveQueryRequest(state.Query, cursor, Limit: 1);
                var page = await RequireAsync(reader, state, request, route, 1, [changes[index]], changes[index].Sequence, index < changes.Length - 1, token);
                await RequireAsync(reader, state, request, route, 1, [changes[index]], changes[index].Sequence, index < changes.Length - 1, token);
                cursor = page.Cursor;
            }
            await RequireAsync(reader, state, new(state.Query, cursor), route, 1, [], 5L, false, token);
        }
    }
    internal static async Task FreshAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state, CancellationToken token)
    {
        var expected = new LiveQueryChange(6L, state.Commands[2].Receipt.Token, LiveQueryChangeKind.Upsert,
            state.StoredReference("c"), 1L, DocumentLiveProfileClusterState.Row("c", 1L, 5));
        for (var route = 0; route < 4; route++)
        {
            var request = new ReadLiveQueryRequest(state.Query, state.FreshCursor, Limit: 1);
            var page = await RequireAsync(reader, state, request, route, 2, [expected], 6L, false, token);
            await RequireAsync(reader, state, request, route, 2, [expected], 6L, false, token);
            await RequireAsync(reader, state, request with
            {
                Cursor = page.Cursor
            }, route, 2, [], 6L, false, token);
        }
    }
    private static async Task<LiveQueryPage> RequireAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state,
        ReadLiveQueryRequest request, int route, int phase, LiveQueryChange[] changes, long through, bool more, CancellationToken token)
    {
        var before = await DocumentLiveProfileClusterValues.CutAsync(reader, state, phase, token);
        var page = await DocumentLiveProfileClusterCalls.ReadAsync(reader, state, request, route, token);
        await LiveQueryCutClusterAssertions.BoundsAsync(before, page.CutPosition, await DocumentLiveProfileClusterValues.CutAsync(reader, state, phase, token));
        await Assert.That(page.Cursor).IsNotEmpty();
        await SqlClusterProtocol.EqualAsync(new LiveQueryPage([.. changes], page.Cursor, through, more, page.CutPosition), page);
        return page;
    }
}
