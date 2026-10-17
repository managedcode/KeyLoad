using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Pages
{
    internal static async Task<string> SnapshotAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state, int phase, CancellationToken token)
    {
        var cursor = string.Empty;
        for (var route = 0; route < 4; route++)
        {
            var before = await DocumentLiveProfileRf3Values.CutAsync(reader, state, phase, token);
            var page = await DocumentLiveProfileRf3Calls.StartAsync(reader, state, state.Query, route, token);
            await LiveQueryCutRf3Assertions.BoundsAsync(before, page.CutPosition, await DocumentLiveProfileRf3Values.CutAsync(reader, state, phase, token));
            await Assert.That(page.Cursor).IsNotEmpty();
            await SqlRf3Protocol.EqualAsync(new LiveQuerySnapshot([.. DocumentLiveProfileRf3State.Rows(phase)], page.Cursor, phase == 0 ? 2L : phase == 1 ? 5L : 6L, page.CutPosition), page);
            cursor = page.Cursor;
        }
        return cursor;
    }
    internal static async Task TailAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state, CancellationToken token)
    {
        var receipt = state.Commands[1].Receipt;
        LiveQueryChange[] changes =
        [new(3L, receipt.Token, LiveQueryChangeKind.Upsert, state.Reference("a"), 2L, DocumentLiveProfileRf3State.Row("a", 2L, 2)),
         new(4L, receipt.Token, LiveQueryChangeKind.Upsert, state.Reference("b"), 1L, DocumentLiveProfileRf3State.Row("b", 1L, 3)),
         new(5L, receipt.Token, LiveQueryChangeKind.Remove, state.Reference("a"), 3L, null)];
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
    internal static async Task FreshAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state, CancellationToken token)
    {
        var expected = new LiveQueryChange(6L, state.Commands[2].Receipt.Token, LiveQueryChangeKind.Upsert,
            state.Reference("c"), 1L, DocumentLiveProfileRf3State.Row("c", 1L, 5));
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
    private static async Task<LiveQueryPage> RequireAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state,
        ReadLiveQueryRequest request, int route, int phase, LiveQueryChange[] changes, long through, bool more, CancellationToken token)
    {
        var before = await DocumentLiveProfileRf3Values.CutAsync(reader, state, phase, token);
        var page = await DocumentLiveProfileRf3Calls.ReadAsync(reader, state, request, route, token);
        await LiveQueryCutRf3Assertions.BoundsAsync(before, page.CutPosition, await DocumentLiveProfileRf3Values.CutAsync(reader, state, phase, token));
        await Assert.That(page.Cursor).IsNotEmpty();
        await SqlRf3Protocol.EqualAsync(new LiveQueryPage([.. changes], page.Cursor, through, more, page.CutPosition), page);
        return page;
    }
}
