using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class JournalOriginalFailureRecovery
{
    private const int ColdCycles = 2;

    internal static async Task RunAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OpenAsync(files, lifetime, commitHealthy: true), failures);
        for (var cold = 0; cold < ColdCycles && lifetime.CanAdvance; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => OpenAsync(files, lifetime, commitHealthy: false), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OpenAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime, bool commitHealthy)
    {
        var failures = new List<Exception>();
        var store = lifetime.Acquire(() => new ZoneTreeStore(files.Options,
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(OrleansSerialization.Serialize(store.Identity)
                    .AsSpan().SequenceEqual(OrleansSerialization.Serialize(files.Identity))).IsTrue();
                if (commitHealthy)
                {
                    await JournalOriginalFailureCut.RowsAsync(store, files, healthy: false);
                    JournalOriginalFailureCut.CommitHealthy(store);
                }
                await JournalOriginalFailureCut.RowsAsync(store, files, healthy: true);
            }, failures);
            lifetime.Close(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
