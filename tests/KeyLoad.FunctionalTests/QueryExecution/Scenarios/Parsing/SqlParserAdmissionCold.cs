using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlParserAdmissionCold
{
    private const int ColdCycles = 2;
    internal static async Task RunAsync(SqlParserAdmissionFixture fixture, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = CanonicalStoreImage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < ColdCycles; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(db.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, db.StorageExecution);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
                await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionWave.RunAsync(fixture, db.CreateConfiguredDatabase(store), store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
