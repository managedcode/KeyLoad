using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, ReplicatedOperation operation,
        ErrorCode code, string? detail = null)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = database.Apply(operation);
            await Assert.That(result.Error).IsEqualTo(code);
            await Assert.That(result.Json).IsNull();
            await Assert.That(result.NativeValue).IsNull();
            if (detail is not null)
            {
                await Assert.That(result.SafeDetail).IsEqualTo(detail);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store, bytes,
            position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
