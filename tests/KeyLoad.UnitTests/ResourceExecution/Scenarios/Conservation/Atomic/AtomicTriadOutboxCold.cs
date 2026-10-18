using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadOutboxCold
{
    internal static async Task<AtomicTriadOutboxContinuation> RunAsync(TestDatabase source,
        Func<DatabaseEngine, ZoneTreeStore, Task<AtomicTriadOutboxContinuation>> action)
    {
        AtomicTriadOutboxContinuation? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = ZoneTreeExistingStore.Open(new(source.Directory)
            {
                Incarnation = source.Store.Identity.Incarnation
            }, source.Store.Identity.NodeId, source.StorageExecution);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AtomicTriadOutboxImage.ExactAsync(store.Identity, source.Store.Identity);
                result = await action(source.CreateConfiguredDatabase(store), store);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The original whole mixed-model cold operation did not settle.");
    }
}
