using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class AtomicPartitionScopeLifecycleCold
{
    internal static async Task RunAsync(string directory, StoreIdentity originalIdentity, Func<DatabaseEngine, Task> originalBody)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        ServerFailureObserver.Observe(() => store = new ZoneTreeStore(new(directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            DatabaseEngine? database = null;
            ServerFailureObserver.Observe(() =>
            {
                database = QueueWholeFlowStorage.Open(store);
            }, failures);
            if (database is not null && failures.Count == 0)
            {
                await ServerFailureObserver.ObserveAsync(() => RequireOriginalIdentityAsync(store, originalIdentity), failures).ConfigureAwait(false);
                if (failures.Count == 0)
                {
                    await ServerFailureObserver.ObserveAsync(() => originalBody(database), failures).ConfigureAwait(false);
                }
            }
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RequireOriginalIdentityAsync(ZoneTreeStore store, StoreIdentity originalIdentity)
    {
        await Assert.That(store.Identity.Incarnation).IsEqualTo(originalIdentity.Incarnation);
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(originalIdentity))).IsTrue();
    }
}
