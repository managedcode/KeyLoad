using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileFlow
{
    internal static async Task RunAsync(ModelQuerySourceKind kind, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new DocumentLiveProfileFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                var store = fixture.Harness.Store;
                var queries = new QueryEngine(fixture.Harness.Database, UnitExecutionOptions.QueryExecution());
                var initial = await DocumentLiveProfilePages.SnapshotAsync(fixture, queries, store, false, token);
                await DocumentLiveProfileReads.ModelsAsync(fixture, queries, store, kind, initial.Cursor, token);
                await DocumentLiveProfileReads.CancelledAsync(fixture, queries, store, initial.Cursor);
                await fixture.CommitAsync([new PutDocument(DocumentLiveProfileFixture.Collection, "a", DocumentLiveProfileFixture.Json(2), 1L, Access: new("live-owner")),
                    new PutDocument(DocumentLiveProfileFixture.Collection, "b", DocumentLiveProfileFixture.Json(3), 0L, Access: new("live-owner")),
                    new DeleteDocument(DocumentLiveProfileFixture.Collection, "a", 2L)],
                    [new("putDocument", DocumentLiveProfileFixture.Collection, "a", 2L), new("putDocument", DocumentLiveProfileFixture.Collection, "b", 1L),
                        new("deleteDocument", DocumentLiveProfileFixture.Collection, "a", 3L)], token);
                await DocumentLiveProfileReads.ObserveAsync(store, () => DocumentLiveProfileReads.RefuseAsync(() =>
                    queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, initial.Cursor, MaxBytes: 1), cancellationToken: token),
                    ErrorCode.BudgetExceeded, "The first change exceeds the page byte budget."));
                await DocumentLiveProfilePages.TailAsync(fixture, queries, store, initial.Cursor, token);
                var fresh = await DocumentLiveProfilePolicy.RunAsync(fixture, queries, initial.Cursor, token);
                await fixture.CommitAsync([new PutDocument(DocumentLiveProfileFixture.Collection, "c", DocumentLiveProfileFixture.Json(5), 0L, Access: new("live-owner"))],
                    [new("putDocument", DocumentLiveProfileFixture.Collection, "c", 1L)], token);
                await DocumentLiveProfilePages.FreshAsync(fixture, queries, store, fresh, token);
                await DocumentLiveProfileReads.ReplayAsync(fixture, fixture.Harness.Database, store, token);
                await DocumentLiveProfileCold.RunAsync(fixture, kind, initial.Cursor, fresh, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
