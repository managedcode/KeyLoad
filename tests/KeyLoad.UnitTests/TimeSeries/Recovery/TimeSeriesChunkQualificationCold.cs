using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class TimeSeriesChunkQualificationCold
{
    internal static async Task RunAsync(TimeSeriesChunkQualificationFixture fixture, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = CanonicalStoreImage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < TimeSeriesChunkQualificationData.ColdCycles; cycle++)
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
                    await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationFlow.WaveAsync(fixture, database, store, token), failures);
                    await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut, bytes), failures);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
