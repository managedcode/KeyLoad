using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class JournalOriginalFailureFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => FailOriginalAsync(files, lifetime), failures);
            if (lifetime.CanAdvance)
            {
                await ServerFailureObserver.ObserveAsync(() => JournalOriginalFailureRecovery.RunAsync(files, lifetime), failures);
            }
            if (lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FailOriginalAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime)
    {
        var failures = new List<Exception>();
        ZoneTreeStoreRuntime? pending = null;
        ZoneTreeStoreRuntime? original = null;
        ZoneTreeStore? store = null;
        ServerFailureObserver.Observe(() =>
        {
            pending = lifetime.Acquire(() => new ZoneTreeStoreRuntime(
                files.Options.ResolveExecutionOptions(UnitExecutionOptions.StorageExecution())), failures);
            if (pending is null)
            {
                return;
            }
            store = new(pending, pending.Identity.NodeId);
            lifetime.Transfer(pending, store);
            original = pending;
            pending = null;
        }, failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => JournalOriginalFailureAssertions.FailAsync(original!, store, files), failures);
            lifetime.Close(store, failures);
        }
        if (pending is not null)
        {
            lifetime.Close(pending, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
