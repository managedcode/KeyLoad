using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutReplay
{
    internal static async Task ProcessingAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state)
    {
        var failures = new List<Exception>();
        foreach (var item in state.Successes.Where(pair => pair.Key.Value!.Kind is OperationKind.SubscriptionProcessing
            or OperationKind.SubscriptionDelivery or OperationKind.Batch))
        {
            var bytes = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var key = KeySpace.PartitionOutcome(state.Group.Source.Partition, item.Key.UserId!, item.Key.CommandId);
                var raw = store.Read(view => view.ReadOwnedValue(key))
                    ?? throw new InvalidOperationException("The original outcome is missing.");
                var replay = database.Apply(item.Key);
                var stored = OrleansSerialization.Deserialize<StoredOutcome>(raw).Result;
                if (item.Key.Value!.Kind == OperationKind.SubscriptionProcessing)
                {
                    await SameProcessingAsync(replay, item.Value);
                    await SameProcessingAsync(stored, item.Value);
                }
                else
                {
                    await ReplayResultAssertions.Same<CommitReceipt>(replay, item.Value);
                    await ReplayResultAssertions.Same<CommitReceipt>(stored, item.Value);
                }
                await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(raw)).IsTrue();
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
                bytes, position, identity), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task SameProcessingAsync(Result<object> actual, Result<object> expected)
    {
        await ReplayResultAssertions.Same<SubscriptionProcessingResult>(actual, expected);
        await ReplayResultAssertions.Same<CommitReceipt>(Result<object>.Succeed(actual.Get<SubscriptionProcessingResult>().Receipt), Result<object>.Succeed(expected.Get<SubscriptionProcessingResult>().Receipt));
    }
}
