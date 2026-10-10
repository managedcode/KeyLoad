using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, new RedriveQueueMessage(QueueOrderedRetryProtocol.Queue, QueueOrderedRetryProtocol.First,
            QueueOrderedRetryProtocol.Six, QueueOrderedRetryProtocol.One)).Get<CommitReceipt>();
        await QueueOrderedRetryAssertions.RedrivenAsync(database, state);
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var stale = QueueOrderedRetryOperations.Complete(database, state, state.FirstLease!, DeliveryAction.Ack);
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.StaleLease);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        if (state.ParkedHead == QueueParkedHeadPolicy.Continue)
        {
            await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty();
            QueueOrderedRetryOperations.Complete(database, state, state.SecondLease!, DeliveryAction.Ack).Get<CommitReceipt>();
        }
        var first = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.First);
        QueueOrderedRetryOperations.Complete(database, state, first, DeliveryAction.Ack).Get<CommitReceipt>();
        if (state.ParkedHead == QueueParkedHeadPolicy.Block)
        {
            var second = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.Second);
            QueueOrderedRetryOperations.Complete(database, state, second, DeliveryAction.Ack).Get<CommitReceipt>();
        }
    }
}
