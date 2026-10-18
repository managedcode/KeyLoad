using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileCold
{
    internal static async Task RunAsync(DocumentLiveProfileFixture fixture, ModelQuerySourceKind kind,
        string old, string fresh, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = CanonicalStoreImage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = NativeSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(db.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, db.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var database = db.CreateConfiguredDatabase(store);
                    var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                    await DocumentLiveProfileReads.ModelsAsync(fixture, queries, store, kind, fresh, token);
                    await DocumentLiveProfilePages.SnapshotAsync(fixture, queries, store, true, token);
                    await DocumentLiveProfilePages.FreshAsync(fixture, queries, store, fresh, token);
                    await DocumentLiveProfileReads.ObserveAsync(store, () => DocumentLiveProfileReads.RefuseAsync(() =>
                        queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Query, old), cancellationToken: token),
                        ErrorCode.TokenInvalidated, "The change-feed cursor requires a fresh authorized snapshot."));
                    await DocumentLiveProfileReads.ReplayAsync(fixture, database, store, token);
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
