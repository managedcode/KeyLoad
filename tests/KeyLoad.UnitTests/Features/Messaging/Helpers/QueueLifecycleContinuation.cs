using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var resumed = state.CancelledCommand!;
        state.Execute(database, OperationKind.Batch, resumed, resumed.CommandId).Get<CommitReceipt>();
        await SqlPendingQueueAuthorityFlow.RunAsync(database, store, state, token);
        state.Batch(database, new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.DeadLettered,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Two);
        state.Batch(database, new RedriveQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.Ready,
            QueueLifecycleTestProtocol.Five, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        var fresh = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Pending);
        await Assert.That(fresh.DeliveryGeneration).IsEqualTo(QueueLifecycleTestProtocol.Two);
        await QueueLifecycleRefusals.StaleAsync(database, store, state, state.OriginalPending!);
        QueueLifecycleOperations.Complete(database, state, fresh, DeliveryAction.Ack).Get<CommitReceipt>();
        state.Batch(database, new CancelQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Held,
            QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        await QueueLifecycleRefusals.StaleAsync(database, store, state, state.OriginalHeld!);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Parked, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.Acked,
            QueueLifecycleTestProtocol.Seven, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Held, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleAccountingAssertions.TerminalAsync(store, state, false);
    }
}
