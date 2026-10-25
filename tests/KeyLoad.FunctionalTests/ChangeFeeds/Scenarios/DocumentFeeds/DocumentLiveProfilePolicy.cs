using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class DocumentLiveProfilePolicy
{
    internal static async Task<string> RunAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, string cursor, CancellationToken token)
    {
        await fixture.PersistAsync(2L, false, false);
        await RefuseAsync(fixture, queries, cursor, ErrorCode.PermissionDenied,
            "The principal cannot perform this operation in this scope.", token);
        await fixture.PersistAsync(3L, true, true);
        await RefuseAsync(fixture, queries, cursor, ErrorCode.Unauthenticated, "The credential is unavailable or expired.", token);
        await fixture.PersistAsync(4L, true, false);
        await DocumentLiveProfileReads.ObserveAsync(fixture.Harness.Store, () => DocumentLiveProfileReads.RefuseAsync(() =>
            queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor), cancellationToken: token),
            ErrorCode.TokenInvalidated, "The change-feed cursor requires a fresh authorized snapshot."));
        LiveQuerySnapshot? snapshot = null;
        await DocumentLiveProfileReads.ObserveAsync(fixture.Harness.Store, async () =>
        {
            snapshot = queries.StartLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query), cancellationToken: token);
            await Assert.That(snapshot.Cursor).IsNotEmpty();
            await DocumentLiveProfileReads.EqualAsync(snapshot, new LiveQuerySnapshot([DocumentLiveProfileFixture.Row("b", 1L, 3)],
                snapshot.Cursor, 5L, fixture.Harness.Store.Position));
        });
        return snapshot!.Cursor;
    }
    private static async Task RefuseAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, string cursor,
        ErrorCode code, string detail, CancellationToken token)
    {
        await DocumentLiveProfileReads.ObserveAsync(fixture.Harness.Store, () => DocumentLiveProfileReads.RefuseAsync(() =>
            queries.StartLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query), cancellationToken: token), code, detail));
        await DocumentLiveProfileReads.ObserveAsync(fixture.Harness.Store, () => DocumentLiveProfileReads.RefuseAsync(() =>
            queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor), cancellationToken: token), code, detail));
    }
}
