using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RevokedOutcomeAdmissionCold
{
    internal static async Task BothAsync(RevokedOutcomeAdmissionFixture fixture, List<Exception> failures, CancellationToken token)
    {
        string[] image = [];
        StoreIdentity? identity = null;
        long position = 0;
        ServerFailureObserver.Observe(() =>
        {
            image = QueueWholeFlowStorage.Bytes(fixture.Store);
            position = fixture.Store.Position;
            identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        }, failures);
        ServerFailureObserver.Observe(fixture.Store.Dispose, failures);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(() => OneAsync(fixture, image, identity!, position, token), failures);
        }
    }
    private static async Task OneAsync(RevokedOutcomeAdmissionFixture fixture, string[] image,
        StoreIdentity identity, long position, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            store = new(new(fixture.Harness.Directory), fixture.Harness.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            fixture.Store = store;
            fixture.Database = fixture.Harness.CreateConfiguredDatabase(store);
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.ConservedAsync(fixture, image, identity, position), failures);
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionFlow.HealthyAsync(fixture, token), failures);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
