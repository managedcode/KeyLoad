using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeLease
{
    internal static async Task<(MessageMetadata Metadata, DateTimeOffset Due)> CreateAsync(DatabaseEngine database,
        QueueLaneRef lane, MessageMetadata scheduled, DateTimeOffset due)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var received = QueueDeadlineNativeOperations.Apply(database, OperationKind.Receive, request, request.RequestId,
            due, QueueDeadlineNativeProtocol.Worker).Get<ReceiveResult>();
        var delivery = received.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(scheduled.Id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineNativeProtocol.Payload);
        var metadata = scheduled with
        {
            State = MessageState.Leased,
            StateVersion = QueueDeadlineNativeProtocol.Third,
            ReadySequence = QueueDeadlineNativeProtocol.First,
            Attempts = (int)QueueDeadlineNativeProtocol.First,
            LeaseVersion = QueueDeadlineNativeProtocol.First,
            NotBefore = null,
            LeaseOwner = QueueDeadlineNativeProtocol.Worker,
            LeaseUntil = delivery.LeaseUntil
        };
        await QueueDeadlineNativeAssertions.OriginalAsync(database, lane, metadata);
        return (metadata, delivery.LeaseUntil);
    }
}
