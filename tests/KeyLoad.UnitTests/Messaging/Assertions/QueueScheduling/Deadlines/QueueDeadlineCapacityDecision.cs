using KeyLoad.Core.Messaging;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadlineCapacityDecision
{
    internal static async Task<MessageMetadata> ExpectedAsync(QueueDeadlineCapacityState state, Command<DatabaseCommand> operation)
    {
        var original = state.Original;
        if (state.Kind == QueueDeadlineKind.ExpireMessage || state.Fault == QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark)
        {
            return original with
            {
                State = MessageState.Expired,
                StateVersion = original.StateVersion + StateVersionStep,
                LeaseOwner = null,
                LeaseUntil = null,
                ActiveOrderSequence = NoOrderSequence
            };
        }
        if (state.Fault is QueueDeadlineCapacityFault.ParkedSequence or QueueDeadlineCapacityFault.FullDeadLetterMaxPark)
        {
            var pending = state.Fault == QueueDeadlineCapacityFault.FullDeadLetterMaxPark;
            return original with
            {
                State = pending ? MessageState.PendingDeadLetter : MessageState.DeadLettered,
                StateVersion = original.StateVersion + StateVersionStep,
                LeaseOwner = null,
                LeaseUntil = null,
                NotBefore = null,
                SafeFailureCode = AttemptsExhausted,
                ParkedSequence = pending ? NoParkedSequence : FirstParkedSequence
            };
        }
        if (state.Jitter)
        {
            var wrapper = operation.Value!;
            var choice = OrleansSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span).Items.Single();
            await Assert.That(choice.Lane).IsEqualTo(state.Lane);
            await Assert.That(choice.MessageId).IsEqualTo(original.Id);
            await Assert.That(choice.ExpectedStateVersion).IsEqualTo(original.StateVersion);
            await Assert.That(choice.ExpectedLeaseVersion).IsEqualTo(original.LeaseVersion);
            await Assert.That(choice.DeliveryGeneration).IsEqualTo(original.DeliveryGeneration);
            await Assert.That(choice.Attempts).IsEqualTo(original.Attempts);
            await Assert.That(choice.DueIndexKey.ToArray()).IsEquivalentTo(KeySpace.Partition(Lease,
                state.Lane.Partition, Queue, state.Due, Message), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(choice.DelayMilliseconds).IsGreaterThanOrEqualTo(MinimumJitterMilliseconds);
            await Assert.That(choice.DelayMilliseconds).IsLessThanOrEqualTo(RetryCapMilliseconds);
            await Assert.That(choice.RetryAt).IsEqualTo(state.Due.AddMilliseconds(choice.DelayMilliseconds));
            return original with
            {
                State = MessageState.Scheduled,
                NotBefore = choice.RetryAt,
                StateVersion = original.StateVersion + StateVersionStep,
                LeaseOwner = null,
                LeaseUntil = null,
                SafeFailureCode = RetryFailureCode
            };
        }
        return original with
        {
            State = MessageState.Ready,
            NotBefore = null,
            StateVersion = original.StateVersion + StateVersionStep,
            LeaseOwner = null,
            LeaseUntil = null
        };
    }
}
