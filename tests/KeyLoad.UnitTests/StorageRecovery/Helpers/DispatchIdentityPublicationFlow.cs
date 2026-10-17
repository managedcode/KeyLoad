using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class DispatchIdentityPublicationFlow
{
    private const int ColdCycles = 2;

    internal static async Task RunAsync(bool initiallyPaused)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(files, lifetime, initiallyPaused), failures);
            if (lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime, bool initiallyPaused)
    {
        var failures = new List<Exception>();
        StoreIdentity? expected = null;
        var store = lifetime.Acquire(() => Open(files), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                expected = await RefuseAndRepairAsync(store, files, initiallyPaused);
            }, failures);
            lifetime.Close(store, failures);
        }
        if (expected is not null && lifetime.CanAdvance)
        {
            for (var cold = 0; cold < ColdCycles && lifetime.CanAdvance; cold++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(files, expected, lifetime), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<StoreIdentity> RefuseAndRepairAsync(ZoneTreeStore store,
        ZoneTreeExistingStoreFixture files, bool initiallyPaused)
    {
        store.SetDispatchPaused(initiallyPaused);
        var original = store.Identity;
        var bytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var temporary = files.IdentityPath + ZoneTreePersistenceFormat.TemporaryFileSuffix;
        Directory.CreateDirectory(temporary);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = Assert.ThrowsExactly<UnauthorizedAccessException>(() => store.SetDispatchPaused(!initiallyPaused));
            await Assert.That(failure).IsNotNull();
            await DispatchIdentityPublicationAssertions.IdentityAsync(store, files.IdentityPath, original);
            await Assert.That((await File.ReadAllBytesAsync(files.IdentityPath)).AsSpan().SequenceEqual(bytes)).IsTrue();
            await DispatchIdentityPublicationAssertions.RowsAsync(store, files, healthy: false);
        }, failures);
        ServerFailureObserver.Observe(() => Directory.Delete(temporary), failures);
        var expected = original with
        {
            DispatchPaused = !initiallyPaused
        };
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store.SetDispatchPaused(!initiallyPaused);
            await DispatchIdentityPublicationAssertions.IdentityAsync(store, files.IdentityPath, expected);
            DispatchIdentityPublicationAssertions.CommitHealthy(store);
            await DispatchIdentityPublicationAssertions.RowsAsync(store, files, healthy: true);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return expected;
    }

    private static async Task ColdAsync(ZoneTreeExistingStoreFixture files, StoreIdentity expected, StorageFailureOwnerLifetime lifetime)
    {
        var failures = new List<Exception>();
        var store = lifetime.Acquire(() => Open(files), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await DispatchIdentityPublicationAssertions.IdentityAsync(store, files.IdentityPath, expected);
                await DispatchIdentityPublicationAssertions.RowsAsync(store, files, healthy: true);
            }, failures);
            lifetime.Close(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ZoneTreeStore Open(ZoneTreeExistingStoreFixture files)
        => new(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
}
