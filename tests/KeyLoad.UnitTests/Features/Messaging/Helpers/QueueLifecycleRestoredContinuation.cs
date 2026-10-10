using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleRestoredContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var resumed = state.Execute(database, OperationKind.SetDispatch, false, Guid.NewGuid()).Get<bool>();
        await Assert.That(resumed).IsTrue();
        state.Batch(database,
            new CancelQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One),
            new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One),
            new RedriveQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        state.Batch(database, new CancelQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Held,
            QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        var delivery = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Pending);
        QueueLifecycleOperations.Complete(database, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Parked, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.Acked,
            QueueLifecycleTestProtocol.Seven, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Held, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleAccountingAssertions.TerminalAsync(store, state, false);
    }
}
