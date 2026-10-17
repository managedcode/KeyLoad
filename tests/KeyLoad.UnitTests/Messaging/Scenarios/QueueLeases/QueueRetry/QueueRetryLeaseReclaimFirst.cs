using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryLeaseReclaimFirst
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, Delivery original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Time = original.LeaseUntil;
        await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty();
        var wrapper = NativeSerialization.Deserialize<NativeCommandPayload>(state.Outcomes.Last().Operation.NativePayload.Span);
        var choices = NativeSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span);
        var choice = await Assert.That(choices.Items).HasSingleItem();
        await Assert.That(choice.DueIndexKey.ToArray()).IsEquivalentTo(KeySpace.Partition("lease", state.Partition,
            state.Lane.Queue, original.LeaseUntil, original.Id), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Scheduled,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.One, choice.RetryAt, null,
            LeaseVersion: QueueOrderedRetryProtocol.One, SafeFailureCode: "RetryRequested",
            EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: QueueOrderedRetryProtocol.One), stored: true);
        await QueueRetryLeaseRaceRefusals.OldOwnerAsync(database, state, original);
        state.Time = choice.RetryAt;
        var fresh = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.First);
        await Assert.That(fresh.LeaseVersion).IsEqualTo(QueueOrderedRetryProtocol.Two);
        await QueueRetryLeaseRaceRefusals.OldOwnerAsync(database, state, original);
        QueueOrderedRetryOperations.Complete(database, state, fresh, DeliveryAction.Ack).Get<CommitReceipt>();
    }
}
