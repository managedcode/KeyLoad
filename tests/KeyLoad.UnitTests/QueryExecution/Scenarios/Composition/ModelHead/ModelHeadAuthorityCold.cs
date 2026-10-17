using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ModelHeadAuthorityCold
{
    internal static async Task RunAsync(ModelHeadAuthorityFixture fixture, int fault, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = GraphShortestPathFullStoreImage.Bytes(db.Store);
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
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut + cycle * 2L, bytes), failures);
                var database = db.CreateConfiguredDatabase(store);
                await ServerFailureObserver.ObserveAsync(() => ModelHeadAuthorityFault.RunAsync(fixture, database, store, fault, token), failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut + (cycle + 1L) * 2L, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
