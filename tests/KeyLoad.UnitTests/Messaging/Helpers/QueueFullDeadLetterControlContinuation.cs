using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueFullDeadLetterControlContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLifecycleTestState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, new CancelQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Held,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        var cancel = state.CancelledCommand!;
        state.Execute(database, OperationKind.Batch, cancel, cancel.CommandId).Get<CommitReceipt>();
        state.Batch(database, new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        state.Batch(database, new RedriveQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.One)).Get<CommitReceipt>();
        var lease = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Pending);
        QueueLifecycleOperations.Complete(database, state, lease, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueLifecycleRefusals.StaleAsync(database, store, state, state.OriginalHeld!);
        await QueueLifecycleRefusals.StaleAsync(database, store, state, state.OriginalPending!);
        await QueueLifecycleHealthy.RunAsync(database, state, token);
    }
}
