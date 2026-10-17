using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleHealthy
{
    internal static EnqueueMessage Enqueue(string id) => new(QueueLifecycleTestProtocol.Queue, id,
        QueueLifecycleTestProtocol.Payload, QueueLifecycleTestProtocol.Headers, OrderingKey: QueueLifecycleTestProtocol.OrderingKey);

    internal static async Task RunAsync(DatabaseEngine database, QueueLifecycleTestState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, Enqueue(QueueLifecycleTestProtocol.Healthy)).Get<CommitReceipt>();
        var delivery = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Healthy);
        QueueLifecycleOperations.Complete(database, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Healthy, MessageState.Acked,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.None);
        await QueueLifecycleOperations.ReplayAsync(database, state);
    }
}
