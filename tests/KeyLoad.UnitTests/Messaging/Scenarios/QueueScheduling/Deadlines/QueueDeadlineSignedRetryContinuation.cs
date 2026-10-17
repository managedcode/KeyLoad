using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineSignedRetryContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata scheduled,
        Delivery original, DateTimeOffset due)
    {
        var oldAck = new DeliveryCommand(Guid.NewGuid(), lane, original.Token, DeliveryAction.Ack);
        var denied = QueueDeadlineNativeOperations.Apply(database, OperationKind.Delivery, oldAck,
            oldAck.CommandId, due, QueueDeadlineNativeProtocol.Worker);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.StaleLease);
        await QueueDeadlineNativeAssertions.OriginalAsync(database, lane, scheduled);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [new AdvanceQueueDeadline(lane.Queue,
            scheduled.Id, scheduled.State, scheduled.StateVersion, scheduled.LeaseVersion, due,
            QueueDeadlineKind.PromoteScheduled)]);
        var result = QueueDeadlineNativeOperations.Batch(database, command, due);
        result.Get<CommitReceipt>();
        var ready = scheduled with
        {
            State = MessageState.Ready,
            StateVersion = scheduled.StateVersion + QueueDeadlineNativeProtocol.First,
            ReadySequence = QueueDeadlineNativeProtocol.Second,
            NotBefore = null
        };
        await QueueDeadlineNativeAssertions.OriginalAsync(database, lane, ready);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Batch(database, command, due), result);
        await QueueDeadlineNativeHealthy.RunAsync(database, lane, due, QueueDeadlineNativeProtocol.Second);
    }
}
