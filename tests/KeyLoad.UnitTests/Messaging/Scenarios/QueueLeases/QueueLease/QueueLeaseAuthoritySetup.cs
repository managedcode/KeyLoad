using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

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
        var claim = Create(fixture.Database, OperationKind.Receive, request, request.RequestId, start);
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
            Original(fixture.Database, lane, kind, delivery.Token, start.AddSeconds(QueueLeaseAuthorityProtocol.One)),
            kind, metadata, counters, bodyBytes, key, bytes, fixture.ExpectedReference(new(lane.Partition,
                QueueLeaseAuthorityProtocol.Collection, QueueLeaseAuthorityProtocol.Document)));
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
            var body = OrleansSerialization.Deserialize<MessageBody>(state.OriginalBytes);
            return OrleansSerialization.Serialize(body with
            {
                Id = QueueLeaseAuthorityProtocol.Foreign
            });
        }
        var metadata = OrleansSerialization.Deserialize<MessageMetadata>(state.OriginalBytes);
        var damaged = fault switch
        {
            QueueLeaseAuthorityFault.ForeignMetadataIdentity => metadata with
            {
                Id = QueueLeaseAuthorityProtocol.Foreign
            },
            QueueLeaseAuthorityFault.MissingMetadataDeadline => metadata with
            {
                LeaseUntil = null
            },
            QueueLeaseAuthorityFault.NegativeAttempts => metadata with
            {
                Attempts = -1
            },
            QueueLeaseAuthorityFault.ZeroAttempts => metadata with
            {
                Attempts = 0
            },
            QueueLeaseAuthorityFault.NegativeStateVersion => metadata with
            {
                StateVersion = -1
            },
            QueueLeaseAuthorityFault.ZeroStateVersion => metadata with
            {
                StateVersion = 0
            },
            QueueLeaseAuthorityFault.UnclaimedStateVersion => metadata with
            {
                StateVersion = 1
            },
            QueueLeaseAuthorityFault.NegativeReadySequence => metadata with
            {
                ReadySequence = -1
            },
            QueueLeaseAuthorityFault.ZeroReadySequence => metadata with
            {
                ReadySequence = 0
            },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        return OrleansSerialization.Serialize(damaged);
    }

    internal static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, T value, Guid id, DateTimeOffset time)
        => database.CreateCommand(kind, id, QueueLeaseAuthorityProtocol.Root, time, JsonSerializer.Serialize(value, JsonDefaults.Options));

    private static byte[] FaultKey(QueueLaneRef lane, QueueLeaseAuthorityFault fault, byte[] bodyKey, DateTimeOffset deadline)
        => fault switch
        {
            QueueLeaseAuthorityFault.ForeignBodyIdentity => bodyKey,
            QueueLeaseAuthorityFault.ForeignMetadataIdentity or QueueLeaseAuthorityFault.MissingMetadataDeadline
                or QueueLeaseAuthorityFault.NegativeAttempts or QueueLeaseAuthorityFault.ZeroAttempts
                or QueueLeaseAuthorityFault.NegativeStateVersion or QueueLeaseAuthorityFault.ZeroStateVersion
                or QueueLeaseAuthorityFault.UnclaimedStateVersion or QueueLeaseAuthorityFault.NegativeReadySequence
                or QueueLeaseAuthorityFault.ZeroReadySequence
                => KeySpace.Partition(QueueLeaseAuthorityProtocol.MetadataSpace, lane.Partition, lane.Queue, QueueLeaseAuthorityProtocol.Message),
            _ => KeySpace.Partition(QueueLeaseAuthorityProtocol.LeaseSpace, lane.Partition, lane.Queue, deadline, QueueLeaseAuthorityProtocol.Message)
        };

    private static Command<DatabaseCommand> Original(DatabaseEngine database, QueueLaneRef lane, QueueLeaseAuthorityOperation kind, string token, DateTimeOffset time)
    {
        var id = Guid.NewGuid();
        if (kind == QueueLeaseAuthorityOperation.Processing)
        {
            return Create(database, OperationKind.Processing, new ProcessingRequest(id, lane, token, QueueLeaseAuthorityProtocol.Handler,
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
        return Create(database, OperationKind.Delivery, new DeliveryCommand(id, lane, token, action,
            kind == QueueLeaseAuthorityOperation.Renew ? QueueLeaseAuthorityProtocol.LeaseSeconds : null), id, time);
    }
}
