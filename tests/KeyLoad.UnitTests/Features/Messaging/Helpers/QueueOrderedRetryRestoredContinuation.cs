using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryRestoredContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(state.Execute(database, OperationKind.SetDispatch, false, Guid.NewGuid()).Get<bool>()).IsTrue();
        var secondVersion = state.ParkedHead == QueueParkedHeadPolicy.Continue ? QueueOrderedRetryProtocol.Two : QueueOrderedRetryProtocol.One;
        state.Batch(database,
            new CancelQueueMessage(state.Lane.Queue, QueueOrderedRetryProtocol.Second, secondVersion, QueueOrderedRetryProtocol.One),
            new RedriveQueueMessage(state.Lane.Queue, QueueOrderedRetryProtocol.First, QueueOrderedRetryProtocol.Six, QueueOrderedRetryProtocol.One))
            .Get<CommitReceipt>();
        await QueueOrderedRetryAssertions.RedrivenAsync(database, state);
        var lease = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.First);
        QueueOrderedRetryOperations.Complete(database, state, lease, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
    }
}
