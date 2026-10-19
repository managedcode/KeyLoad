using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineLease
{
    internal static async Task<(MessageMetadata Metadata, DateTimeOffset Due)> CreateAsync(DatabaseEngine database,
        QueueLaneRef lane, MessageMetadata scheduled, DateTimeOffset due)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var received = QueueDeadlineOperations.Apply(database, OperationKind.Receive, request, request.RequestId,
            due, RuntimeQueueDeadlineProtocol.Worker).Get<ReceiveResult>();
        var delivery = received.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(scheduled.Id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(RuntimeQueueDeadlineProtocol.Payload);
        var metadata = scheduled with
        {
            State = MessageState.Leased,
            StateVersion = RuntimeQueueDeadlineProtocol.Third,
            ReadySequence = RuntimeQueueDeadlineProtocol.First,
            Attempts = (int)RuntimeQueueDeadlineProtocol.First,
            LeaseVersion = RuntimeQueueDeadlineProtocol.First,
            NotBefore = null,
            LeaseOwner = RuntimeQueueDeadlineProtocol.Worker,
            LeaseUntil = delivery.LeaseUntil
        };
        await QueueDeadlineAssertions.OriginalAsync(database, lane, metadata);
        return (metadata, delivery.LeaseUntil);
    }
}
