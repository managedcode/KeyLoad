using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePendingAuthorityLeased
{
    internal static async Task RequireAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        Delivery delivery, int terminalVersion)
    {
        var original = QueuePendingAuthorityHistory.Capture(store);
        var sequence = delivery.Id == QueuePendingAuthorityProtocol.History ? 3
            : delivery.Id == QueueLifecycleTestProtocol.Pending ? 4 : 5;
        var expected = new MessageMetadata(delivery.Id, MessageState.Leased, 1, terminalVersion - 1, sequence,
            null, null, QueueLifecycleTestProtocol.Administrator, delivery.LeaseVersion, delivery.LeaseUntil,
            delivery.DeliveryGeneration);
        await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Flow.Lane, delivery.Id))
            .IsEqualTo(new MessageInspection(expected, QueueLifecycleTestProtocol.Payload, QueueLifecycleTestProtocol.Headers));
        await Assert.That(store.Read(view => view.GetRecord<MessageMetadata>(QueuePendingAuthorityOperations.Key(state.Flow,
            QueueLifecycleTestProtocol.MetadataSpace, delivery.Id)))).IsEqualTo(expected);
        var literal = QueueLifecycleHealthy.Enqueue(delivery.Id);
        var body = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey,
            JsonData.Fingerprint(literal));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(QueuePendingAuthorityOperations.Key(state.Flow,
            QueueLifecycleTestProtocol.BodySpace, delivery.Id)))).IsEqualTo(body);
        await Assert.That(store.Read(view => view.GetRecord<string>(QueuePendingAuthorityOperations.Key(state.Flow,
            QueueLifecycleTestProtocol.LeaseSpace, delivery.LeaseUntil, delivery.Id)))).IsEqualTo(delivery.Id);
        var counters = store.Read(view => view.GetRecord<QueueCounters>(QueuePendingAuthorityOperations.Key(state.Flow,
            QueueLifecycleTestProtocol.CounterSpace)))!;
        await Assert.That(counters.InFlightMessages).IsEqualTo(1);
        await Assert.That(counters.InFlightBytes).IsEqualTo(OrleansSerialization.Serialize(body).LongLength);
        await QueuePendingAuthorityHistory.SameAsync(store, original);
    }
}
