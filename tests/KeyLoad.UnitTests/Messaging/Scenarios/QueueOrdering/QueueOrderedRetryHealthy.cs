using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Healthy)).Get<CommitReceipt>();
        var lease = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.Healthy);
        QueueOrderedRetryOperations.Complete(database, state, lease, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
    }
}
