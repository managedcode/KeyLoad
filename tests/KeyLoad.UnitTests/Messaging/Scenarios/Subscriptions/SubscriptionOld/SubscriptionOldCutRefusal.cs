using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.StorageRecovery.Assertions;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionOldCutRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> operation,
        ErrorCode code, string? detail = null)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = database.Apply(operation);
            await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(code);

            await Assert.That(result.Value).IsNull();
            if (detail is not null)
            {
                await Assert.That(result.Problem?.Detail).IsEqualTo(detail);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store, bytes,
            position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
