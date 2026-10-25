using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class ValueHeaderRecovery
{
    private const int ColdCycles = 2;

    internal static async Task RunAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OpenAsync(files, lifetime, publish: true), failures);
        for (var cold = 0; cold < ColdCycles && lifetime.CanAdvance; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => OpenAsync(files, lifetime, publish: false), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OpenAsync(ZoneTreeExistingStoreFixture files,
        StorageFailureOwnerLifetime lifetime, bool publish)
    {
        var failures = new List<Exception>();
        var store = lifetime.Acquire(() => new ZoneTreeStore(files.Options,
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (publish)
                {
                    await ValueHeaderRecords.RowsAsync(store, files, healthy: false);
                    store.Commit((tx, _) =>
                    {
                        tx.Put(ValueHeaderRecords.HealthyKey, ValueHeaderRecords.HealthyValue);
                        return true;
                    });
                }
                await ValueHeaderRecords.RowsAsync(store, files, healthy: true);
                if (publish)
                {
                    await ValueHeaderSnapshot.HealthyAsync(store, files);
                    await ValueHeaderRecords.RowsAsync(store, files, healthy: true);
                }
            }, failures);
            lifetime.Close(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
