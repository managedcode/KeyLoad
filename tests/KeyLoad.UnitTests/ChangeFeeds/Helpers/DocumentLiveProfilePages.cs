using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class DocumentLiveProfilePages
{
    internal static async Task<LiveQuerySnapshot> SnapshotAsync(DocumentLiveProfileFixture fixture, QueryEngine queries,
        ZoneTreeStore store, bool final, CancellationToken token)
    {
        LiveQuerySnapshot? result = null;
        await DocumentLiveProfileReads.ObserveAsync(store, async () =>
        {
            result = queries.StartLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query), cancellationToken: token);
            await Assert.That(result.Cursor).IsNotEmpty();
            await DocumentLiveProfileReads.EqualAsync(result, new LiveQuerySnapshot(final
                ? [DocumentLiveProfileFixture.Row("b", 1L, 3), DocumentLiveProfileFixture.Row("c", 1L, 5)]
                : [DocumentLiveProfileFixture.Row("a", 1L, 1)], result.Cursor, final ? 6L : 2L, store.Position));
        });
        return result!;
    }
    internal static async Task TailAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, ZoneTreeStore store,
        string initial, CancellationToken token)
    {
        var cursor = initial;
        var receipt = fixture.Commands[1].Receipt;
        LiveQueryChange[] changes =
        [new(3L, receipt.Token, LiveQueryChangeKind.Upsert, fixture.Reference("a"), 2L, DocumentLiveProfileFixture.Row("a", 2L, 2)),
         new(4L, receipt.Token, LiveQueryChangeKind.Upsert, fixture.Reference("b"), 1L, DocumentLiveProfileFixture.Row("b", 1L, 3)),
         new(5L, receipt.Token, LiveQueryChangeKind.Remove, fixture.Reference("a"), 3L, null)];
        for (var index = 0; index < changes.Length; index++)
        {
            await DocumentLiveProfileReads.ObserveAsync(store, async () =>
            {
                var page = queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor, Limit: 1), cancellationToken: token);
                await Assert.That(page.Cursor).IsNotEmpty();
                await DocumentLiveProfileReads.EqualAsync(page, new LiveQueryPage([changes[index]], page.Cursor,
                    changes[index].Sequence, index < changes.Length - 1, store.Position));
                var duplicate = queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor, Limit: 1), cancellationToken: token);
                await DocumentLiveProfileReads.EqualAsync(duplicate with
                {
                    Cursor = page.Cursor
                }, page);
                cursor = page.Cursor;
            });
        }
        await EmptyAsync(fixture, queries, store, cursor, 5L, token);
    }
    internal static Task EmptyAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, ZoneTreeStore store,
        string cursor, long through, CancellationToken token)
        => DocumentLiveProfileReads.ObserveAsync(store, async () =>
        {
            var page = queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor), cancellationToken: token);
            await DocumentLiveProfileReads.EqualAsync(page, new LiveQueryPage([], page.Cursor, through, false, store.Position));
            await Assert.That(page.Cursor).IsNotEmpty();
        });
    internal static Task FreshAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, ZoneTreeStore store,
        string cursor, CancellationToken token)
        => DocumentLiveProfileReads.ObserveAsync(store, async () =>
        {
            var page = queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor, Limit: 1), cancellationToken: token);
            await DocumentLiveProfileReads.EqualAsync(page, new LiveQueryPage([new(6L, fixture.Commands[2].Receipt.Token,
                LiveQueryChangeKind.Upsert, fixture.Reference("c"), 1L, DocumentLiveProfileFixture.Row("c", 1L, 5))], page.Cursor, 6L, false, store.Position));
            await EmptyAsync(fixture, queries, store, page.Cursor, 6L, token);
        });
}
