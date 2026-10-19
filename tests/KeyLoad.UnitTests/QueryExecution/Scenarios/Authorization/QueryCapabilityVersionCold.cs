using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryCapabilityVersionCold
{
    internal static async Task RunAsync(QueryCapabilityVersionFixture fixture, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = CanonicalStoreImage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
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
                    await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
                    var database = db.CreateConfiguredDatabase(store);
                    await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRefusals.RunAsync(fixture, database, store, false, token), failures);
                    await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionPages.RunAsync(fixture, database, store, token), failures);
                    await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
