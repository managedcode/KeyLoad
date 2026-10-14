using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthoritySetup
{
    internal static async Task<QueueLeaseAuthorityState> SeedAsync(TestDatabase fixture,
        QueueLeaseAuthorityOperation kind, QueueLeaseAuthorityFault fault)
    {
        fixture.Configure(QueueLeaseAuthorityProtocol.Queue, ResourceKind.WorkQueue);
        fixture.Configure(QueueLeaseAuthorityProtocol.Collection, ResourceKind.Collection);
        var lane = new QueueLaneRef(fixture.Partition, QueueLeaseAuthorityProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var message = new EnqueueMessage(lane.Queue, QueueLeaseAuthorityProtocol.Message,
            QueueLeaseAuthorityProtocol.Payload, QueueLeaseAuthorityProtocol.Headers);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, start);
        produced.Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueLeaseAuthorityProtocol.LeaseSeconds);
        var claim = Create(OperationKind.Receive, request, request.RequestId, start);
        var claimed = fixture.Database.Apply(claim);
        var delivery = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery).IsEqualTo(new Delivery(message.MessageId, QueueLeaseAuthorityProtocol.Payload,
            QueueLeaseAuthorityProtocol.Headers, delivery.Token, QueueLeaseAuthorityProtocol.One,
            start.AddSeconds(QueueLeaseAuthorityProtocol.LeaseSeconds), QueueLeaseAuthorityProtocol.One, QueueLeaseAuthorityProtocol.One));
        var metadata = new MessageMetadata(message.MessageId, MessageState.Leased, QueueLeaseAuthorityProtocol.One,
            QueueLeaseAuthorityProtocol.Two, QueueLeaseAuthorityProtocol.One, null, null, QueueLeaseAuthorityProtocol.Root,
            QueueLeaseAuthorityProtocol.One, delivery.LeaseUntil);
        await Assert.That(fixture.Database.InspectMessage(QueueLeaseAuthorityProtocol.Root, lane, message.MessageId)!.Metadata).IsEqualTo(metadata);
        var bodyKey = KeySpace.Partition(QueueLeaseAuthorityProtocol.BodySpace, lane.Partition, lane.Queue, message.MessageId);
        var bodyBytes = fixture.Store.Read(view => view.ReadOwnedValue(bodyKey)!.LongLength);
        var counters = new QueueCounters(QueueLeaseAuthorityProtocol.One, bodyBytes, QueueLeaseAuthorityProtocol.One,
            bodyBytes, QueueLeaseAuthorityProtocol.One);
        await Assert.That(Counters(fixture.Store, lane)).IsEqualTo(counters);
        var key = FaultKey(lane, fault, bodyKey, delivery.LeaseUntil);
        var bytes = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        return new(lane, start, message, producer, produced, claim, claimed, delivery,
            Original(lane, kind, delivery.Token, start.AddSeconds(QueueLeaseAuthorityProtocol.One)),
            kind, metadata, counters, bodyBytes, key, bytes);
    }

    internal static QueueCounters Counters(ZoneTreeStore store, QueueLaneRef lane) => store.Read(view =>
        view.GetRecord<QueueCounters>(KeySpace.Partition(QueueLeaseAuthorityProtocol.CounterSpace, lane.Partition, lane.Queue))!);

    internal static void Damage(ZoneTreeStore store, QueueLeaseAuthorityState state, QueueLeaseAuthorityFault fault)
        => store.Commit((tx, _) =>
        {
            switch (fault)
            {
                case QueueLeaseAuthorityFault.MissingReference:
                    tx.Delete(state.FaultKey);
                    break;
                case QueueLeaseAuthorityFault.ForeignReference:
                    tx.PutRecord(state.FaultKey, QueueLeaseAuthorityProtocol.Foreign);
                    break;
                default:
                    tx.Put(state.FaultKey, DamagedBytes(state, fault));
                    break;
            }
            return true;
        });

    private static byte[] DamagedBytes(QueueLeaseAuthorityState state, QueueLeaseAuthorityFault fault)
    {
        if (fault == QueueLeaseAuthorityFault.ForeignBodyIdentity)
        {
            var body = NativeSerialization.Deserialize<MessageBody>(state.OriginalBytes);
            return NativeSerialization.Serialize(body with
            {
                Id = QueueLeaseAuthorityProtocol.Foreign
            });
        }
        var metadata = NativeSerialization.Deserialize<MessageMetadata>(state.OriginalBytes);
        return NativeSerialization.Serialize(fault == QueueLeaseAuthorityFault.ForeignMetadataIdentity
            ? metadata with
            {
                Id = QueueLeaseAuthorityProtocol.Foreign
            } : metadata with
            {
                LeaseUntil = null
            });
    }

    internal static ReplicatedOperation Create<T>(OperationKind kind, T value, Guid id, DateTimeOffset time)
        => new(id, kind, QueueLeaseAuthorityProtocol.Root, time, JsonSerializer.Serialize(value, JsonDefaults.Options));

    private static byte[] FaultKey(QueueLaneRef lane, QueueLeaseAuthorityFault fault, byte[] bodyKey, DateTimeOffset deadline)
        => fault switch
        {
            QueueLeaseAuthorityFault.ForeignBodyIdentity => bodyKey,
            QueueLeaseAuthorityFault.ForeignMetadataIdentity or QueueLeaseAuthorityFault.MissingMetadataDeadline
                => KeySpace.Partition(QueueLeaseAuthorityProtocol.MetadataSpace, lane.Partition, lane.Queue, QueueLeaseAuthorityProtocol.Message),
            _ => KeySpace.Partition(QueueLeaseAuthorityProtocol.LeaseSpace, lane.Partition, lane.Queue, deadline, QueueLeaseAuthorityProtocol.Message)
        };

    private static ReplicatedOperation Original(QueueLaneRef lane, QueueLeaseAuthorityOperation kind, string token, DateTimeOffset time)
    {
        var id = Guid.NewGuid();
        if (kind == QueueLeaseAuthorityOperation.Processing)
        {
            return Create(OperationKind.Processing, new ProcessingRequest(id, lane, token, QueueLeaseAuthorityProtocol.Handler,
                QueueLeaseAuthorityProtocol.One, [new PutDocument(QueueLeaseAuthorityProtocol.Collection,
                    QueueLeaseAuthorityProtocol.Document, QueueLeaseAuthorityProtocol.Payload)]), id, time);
        }
        var action = kind switch
        {
            QueueLeaseAuthorityOperation.Ack => DeliveryAction.Ack,
            QueueLeaseAuthorityOperation.Nack => DeliveryAction.Nack,
            QueueLeaseAuthorityOperation.Renew => DeliveryAction.Renew,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return Create(OperationKind.Delivery, new DeliveryCommand(id, lane, token, action,
            kind == QueueLeaseAuthorityOperation.Renew ? QueueLeaseAuthorityProtocol.LeaseSeconds : null), id, time);
    }
}
