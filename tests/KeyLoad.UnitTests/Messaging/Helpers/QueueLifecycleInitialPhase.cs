using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleInitialPhase
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Batch(database, QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Parked),
            QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Pending), QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Held)).Get<CommitReceipt>();
        var first = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Parked);
        QueueLifecycleOperations.Complete(database, state, first, DeliveryAction.Nack).Get<CommitReceipt>();
        var second = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Pending);
        state.OriginalPending = second;
        QueueLifecycleOperations.Complete(database, state, second, DeliveryAction.Nack).Get<CommitReceipt>();
        state.OriginalHeld = await QueueLifecycleOperations.ClaimAsync(database, state, QueueLifecycleTestProtocol.Held);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Parked, MessageState.DeadLettered,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.One);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.PendingDeadLetter,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.None);
        await QueueLifecycleAccountingAssertions.InitialAsync(store, state);
        await QueueLifecycleRefusals.InitialAsync(database, store, state);
        await QueueLifecycleAuthorityRefusal.MissingOrderedReferenceAsync(database, store, state);
    }
}
