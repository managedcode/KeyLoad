using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLeaseAuthorityProcessTrial
{
    internal static async Task RunAsync(string root, bool foreignReference, CancellationToken token)
    {
        using var store = Open(root);
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var original = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.OperationFile), token));
        var delivery = OrleansSerialization.Deserialize<Delivery>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.DeliveryFile), token));
        await Assert.That(delivery).IsEqualTo(new Delivery(QueueOrderedRetryCrashProtocol.First,
            QueueOrderedRetryCrashProtocol.Payload, QueueOrderedRetryCrashProtocol.Headers, delivery.Token,
            1, delivery.LeaseUntil, 1, 1));
        var payload = original.Value!;
        var decisions = OrleansSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        await QueueOrderedRetryRecoveryChoice.AssertAsync(store, original, decisions);
        await Assert.That(OutcomeStoreOracle.Read(store, original)).IsNull();
        await QueueOrderedRetryRecoveryImage.AssertAsync(database, delivery, decisions.Items.Single(), committed: false);
        var key = KeySpace.Partition("lease", QueueOrderedRetryCrashProtocol.Partition,
            QueueOrderedRetryCrashProtocol.Queue, delivery.LeaseUntil, delivery.Id);
        var row = store.Read(view => view.ReadOwnedValue(key))!;
        await Assert.That(OrleansSerialization.Deserialize<string>(row)).IsEqualTo(QueueOrderedRetryCrashProtocol.First);
        var image = QueueLeaseAuthorityProcessImage.Capture(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Commit((tx, _) =>
        {
            if (foreignReference)
            {
                tx.PutRecord(key, QueueOrderedRetryCrashProtocol.Second);
            }
            else
            {
                tx.Delete(key);
            }
            return true;
        });
        var damaged = QueueLeaseAuthorityProcessImage.Capture(store);
        await Assert.That(store.Position).IsEqualTo(position + 1);
        await QueueLeaseAuthorityProcessImage.RefuseAsync(database, store, original, damaged, position + 1, identity);
        await QueueLeaseAuthorityProcessImage.RefuseAsync(database, store, original, damaged, position + 1, identity);
        store.Dispose();
        using var reopened = Open(root);
        var cold = QueueLifecycleRecoveryAssertions.Open(reopened);
        await QueueLeaseAuthorityProcessImage.SameAsync(reopened, damaged, position + 1, identity);
        await QueueLeaseAuthorityProcessImage.RefuseAsync(cold, reopened, original, damaged, position + 1, identity);
        reopened.Commit((tx, _) => { tx.Put(key, row); return true; });
        await QueueLeaseAuthorityProcessImage.SameAsync(reopened, image, position + 2, identity);
        await QueueOrderedRetryRecoveryImage.AssertAsync(cold, delivery, decisions.Items.Single(), committed: false);
        var receipt = cold.Apply(original).Get<CommitReceipt>();
        await QueueOrderedRetryRecoveryImage.AssertAsync(cold, delivery, decisions.Items.Single(), committed: true);
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, cold.ResolveOutcome(original).Get<CommitReceipt>());
        var outcomes = await QueueLeaseAuthorityProcessHealthy.RunAsync(cold, original, receipt, decisions.Items.Single().RetryAt, token);
        var completed = QueueLeaseAuthorityProcessImage.Capture(reopened);
        var finalPosition = reopened.Position;
        reopened.Dispose();
        using var secondCold = Open(root);
        var final = QueueLifecycleRecoveryAssertions.Open(secondCold);
        await QueueLeaseAuthorityProcessImage.SameAsync(secondCold, completed, finalPosition, identity);
        await QueueLeaseAuthorityProcessHealthy.ReplayAsync(final, outcomes);
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(final, healthy: true);
        await QueueLeaseAuthorityProcessImage.EmptyIndexesAsync(final);
        await QueueLeaseAuthorityProcessImage.SameAsync(secondCold, completed, finalPosition, identity);
    }

    private static ZoneTreeStore Open(string root) => new(new(root),
        RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
}
