using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryStrictExpiry
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, DatabaseEngine database,
        QueueOrderedRetryState state, bool renewFirst, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expiry = state.Time.AddSeconds(QueueOrderedRetryProtocol.One);
        state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Expired) with { ExpiresAt = expiry }).Get<CommitReceipt>();
        var original = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.Expired);
        state.Time = original.LeaseUntil;
        await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty();
        var ready = renewFirst ? QueueOrderedRetryProtocol.Three : QueueOrderedRetryProtocol.Four;
        var expected = new MessageMetadata(QueueOrderedRetryProtocol.Expired, MessageState.Expired, QueueOrderedRetryProtocol.One,
            QueueOrderedRetryProtocol.Three, ready, null, expiry, LeaseVersion: QueueOrderedRetryProtocol.One,
            EnqueueSequence: QueueOrderedRetryProtocol.Three);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, expected, stored: false);
        await QueueRetryLeaseRaceRefusals.OldOwnerAsync(database, state, original);
        state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.AfterExpiry)).Get<CommitReceipt>();
        var healthy = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.AfterExpiry);
        QueueOrderedRetryOperations.Complete(database, state, healthy, DeliveryAction.Ack).Get<CommitReceipt>();
        var completed = new MessageMetadata(QueueOrderedRetryProtocol.AfterExpiry, MessageState.Acked, QueueOrderedRetryProtocol.One,
            QueueOrderedRetryProtocol.Three, ready + QueueOrderedRetryProtocol.One, null, null,
            LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: QueueOrderedRetryProtocol.Four);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, completed, stored: false);
        await CountersAsync(database, state, ready);
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
        var image = QueueOrderedRetryImage.Capture(store, state.Lane);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var reopened = QueueWholeFlowStorage.Open(cold);
        await QueueOrderedRetryImage.SameAsync(cold, state.Lane, image);
        await QueueOrderedRetryOperations.ReplayAsync(reopened, state);
        await QueueOrderedRetryAssertions.MessageAsync(reopened, state, expected, stored: false);
        await QueueOrderedRetryAssertions.MessageAsync(reopened, state, completed, stored: false);
        await CountersAsync(reopened, state, ready);
    }
    private static async Task CountersAsync(DatabaseEngine database, QueueOrderedRetryState state, long ready)
    {
        await Assert.That(database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", state.Partition, state.Lane.Queue))))
            .IsEqualTo(new QueueCounters(QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial,
                QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial, ready + QueueOrderedRetryProtocol.One,
                NextOrderSequence: QueueOrderedRetryProtocol.Four));
        var order = database.Store.Read(view => view.Scan(KeySpace.Partition("queue-order", state.Partition, state.Lane.Queue), QueueOrderedRetryProtocol.One));
        await Assert.That(order.Records).IsEmpty();
        await Assert.That(order.HasMore).IsFalse();
    }
}
