using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryInitial
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var missing = state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.MissingKeyId, null));
        await Assert.That(missing.Error).IsEqualTo(ErrorCode.Validation);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.First),
            QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Second),
            QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Other, QueueOrderedRetryProtocol.OtherKey)).Get<CommitReceipt>();
        await QueueOrderedRetryPolicyRefusal.RunAsync(database, state);
        await QueueOrderedRetryAuthorityRefusal.RunAsync(database, state);
        state.FirstLease = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.First);
        await QueueOrderedRetryMissingChoiceRefusal.RunAsync(database, state, state.FirstLease);
        QueueOrderedRetryOperations.Complete(database, state, state.FirstLease, DeliveryAction.Nack).Get<CommitReceipt>();
        var retryAt = await QueueOrderedRetryJitterAssertions.RecordedAsync(database, state);
        var other = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.Other);
        QueueOrderedRetryOperations.Complete(database, state, other, DeliveryAction.Ack).Get<CommitReceipt>();
        state.Time = retryAt.AddTicks(-QueueOrderedRetryProtocol.One);
        await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty();
        state.Time = retryAt;
        var retried = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.First);
        QueueOrderedRetryOperations.Complete(database, state, retried, DeliveryAction.Nack).Get<CommitReceipt>();
        await QueueOrderedRetryAssertions.ParkedAsync(database, state);
        if (state.ParkedHead == QueueParkedHeadPolicy.Continue)
        { state.SecondLease = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.Second); }
        else
        { await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty(); }
    }
}
