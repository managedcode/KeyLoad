using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacityOperations
{
    internal static ReplicatedOperation Due(DatabaseEngine database, QueueLaneRef lane, MessageMetadata metadata,
        DateTimeOffset due, QueueDeadlineKind kind, bool sweep)
    {
        var id = Guid.NewGuid();
        if (sweep)
        {
            return QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Receive, id,
                new ReceiveRequest(id, lane), Root, due);
        }
        var request = new CommandRequest(id, lane.Partition, [new AdvanceQueueDeadline(lane.Queue, metadata.Id,
            metadata.State, metadata.StateVersion, metadata.LeaseVersion, due, kind)]);
        return QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Batch, id, request, Root, due);
    }

    internal static Task ReceiptAsync(OperationResult actual, CommitReceipt expected)
        => NativeReplayResultAssertions.Same<CommitReceipt>(actual, new(null)
        {
            NativeValue = expected
        });

    internal static long ParkedSequence(QueueDeadlineCapacityState state, MessageMetadata metadata)
        => state.Fault is QueueDeadlineCapacityFault.FullDeadLetterMaxPark or QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark
            ? long.MaxValue : metadata.ParkedSequence;
}
