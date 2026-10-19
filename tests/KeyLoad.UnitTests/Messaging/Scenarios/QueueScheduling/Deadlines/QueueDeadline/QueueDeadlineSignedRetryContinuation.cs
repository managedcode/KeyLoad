using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineSignedRetryContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata scheduled,
        Delivery original, DateTimeOffset due)
    {
        var oldAck = new DeliveryCommand(Guid.NewGuid(), lane, original.Token, DeliveryAction.Ack);
        var denied = QueueDeadlineOperations.Apply(database, OperationKind.Delivery, oldAck,
            oldAck.CommandId, due, RuntimeQueueDeadlineProtocol.Worker);
        await Assert.That(Errors.Code(denied.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await QueueDeadlineAssertions.OriginalAsync(database, lane, scheduled);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [new AdvanceQueueDeadline(lane.Queue,
            scheduled.Id, scheduled.State, scheduled.StateVersion, scheduled.LeaseVersion, due,
            QueueDeadlineKind.PromoteScheduled)]);
        var result = QueueDeadlineOperations.Batch(database, command, due);
        result.Get<CommitReceipt>();
        var ready = scheduled with
        {
            State = MessageState.Ready,
            StateVersion = scheduled.StateVersion + RuntimeQueueDeadlineProtocol.First,
            ReadySequence = RuntimeQueueDeadlineProtocol.Second,
            NotBefore = null
        };
        await QueueDeadlineAssertions.OriginalAsync(database, lane, ready);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineOperations.Batch(database, command, due), result);
        await QueueDeadlineHealthy.RunAsync(database, lane, due, RuntimeQueueDeadlineProtocol.Second);
    }
}
