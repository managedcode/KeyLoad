using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxCapacityAuthorityCold
{
    internal static async Task<TargetInboxCapacityAuthorityContinuation> RunAsync(TestDatabase source,
        Func<DatabaseEngine, ZoneTreeStore, Task<TargetInboxCapacityAuthorityContinuation>> operation)
    {
        TargetInboxCapacityAuthorityContinuation? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = ZoneTreeExistingStore.Open(new(source.Directory)
            {
                Incarnation = source.Store.Identity.Incarnation
            }, source.Store.Identity.NodeId, source.StorageExecution);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(source.Store.Identity))).IsTrue();
                result = await operation(source.CreateConfiguredDatabase(store), store);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The complete target inbox capacity flow did not settle.");
    }
}
