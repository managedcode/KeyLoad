using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class HeavyRouteAdmissionCold
{
    internal static async Task RunAsync(HeavyRouteAdmissionFixture fixture, string route, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = SqlInnerJoinRetainedIdentityFlow.Image(db.Store);
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
                    var governor = HeavyRouteAdmissionFlow.Governor();
                    using var admitted = governor.Begin(route, 0, token);
                    admitted.Bind(HeavyRouteAdmissionReads.Authenticate(database, store), token);
                    await HeavyRouteAdmissionReads.PathAsync(fixture, database, store, true, token);
                    await HeavyRouteAdmissionReads.ReplayAsync(fixture, database, store, token);
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => HeavyRouteAdmissionReads.ConserveAsync(store, image, cut, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
