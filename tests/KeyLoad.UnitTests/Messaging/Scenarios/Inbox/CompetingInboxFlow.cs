using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class CompetingInboxFlow
{
    internal static async Task RunAsync(bool equivalent, CancellationToken token)
    {
        TestDatabase? fixture = null;
        CompetingInboxState? state = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new TestDatabase(timeProvider: new ControlledReadClock(SubscriptionOldCutState.Epoch));
            state = new CompetingInboxState(fixture.Partition, SubscriptionOldCutState.Epoch, equivalent);
            await CompetingInboxSeed.RunAsync(fixture, state, token);
        }, failures);
        if (fixture is not null && state is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxAssertions.ReplayAsync(fixture.Database,
                fixture.Store, state), failures);
            string[] bytes = [];
            long position = 0;
            StoreIdentity identity = null!;
            ServerFailureObserver.Observe(() =>
            {
                bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
                position = fixture.Store.Position;
                identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            }, failures);
            ServerFailureObserver.Observe(fixture.Store.Dispose, failures);
            if (bytes.Length > 0)
            {
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(() => ColdAsync(fixture.Directory, state, bytes,
                        position, identity, token), failures);
                }
            }
        }
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ColdAsync(string path, CompetingInboxState state, string[] bytes,
        long position, StoreIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store = new(new(path), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var database = QueueWholeFlowStorage.Open(store);
            await SubscriptionOldCutAssertions.ConservedAsync(store, bytes, position, identity);
            await CompetingInboxAssertions.ReplayAsync(database, store, state);
        }, failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
                bytes, position, identity), failures);
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
