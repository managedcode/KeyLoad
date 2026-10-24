using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueAccountingFaultSetup
{
    internal static async Task<QueueAccountingFaultState> SeedAsync(TestDatabase fixture, QueueAccountingOperation kind)
    {
        fixture.Configure(QueueAccountingFaultProtocol.Queue, ResourceKind.WorkQueue);
        fixture.Configure(QueueAccountingFaultProtocol.Collection, ResourceKind.Collection);
        var lane = new QueueLaneRef(fixture.Partition, QueueAccountingFaultProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var expires = kind is QueueAccountingOperation.ReadyExpiry or QueueAccountingOperation.ScheduledExpiry;
        var message = new EnqueueMessage(lane.Queue, QueueAccountingFaultProtocol.Message,
            QueueAccountingFaultProtocol.Payload, QueueAccountingFaultProtocol.Headers,
            NotBefore: kind == QueueAccountingOperation.ScheduledExpiry ? start.AddSeconds(QueueAccountingFaultProtocol.ScheduledSeconds) : null,
            ExpiresAt: expires ? start.AddSeconds(QueueAccountingFaultProtocol.ExpirySeconds) : null);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, start);
        produced.Get<CommitReceipt>();
        Delivery? delivery = null;
        if (!expires)
        {
            var claim = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueAccountingFaultProtocol.LeaseSeconds);
            delivery = await Assert.That(QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Receive,
                claim, claim.RequestId, start).Get<ReceiveResult>().Deliveries).HasSingleItem();
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueAccountingFaultProtocol.Payload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueAccountingFaultProtocol.Headers);
        }
        var time = expires ? start.AddSeconds(QueueAccountingFaultProtocol.ExpiredEvaluationSeconds)
            : kind == QueueAccountingOperation.Reclaim ? start.AddSeconds(QueueAccountingFaultProtocol.ReclaimEvaluationSeconds) : start;
        var operation = Operation(fixture.Database, lane, kind, delivery?.Token, time);
        var metadata = fixture.Database.InspectMessage(QueueAccountingFaultProtocol.Root, lane, message.MessageId)!.Metadata;
        var scheduled = kind == QueueAccountingOperation.ScheduledExpiry;
        await Assert.That(metadata).IsEqualTo(new MessageMetadata(message.MessageId,
            expires ? scheduled ? MessageState.Scheduled : MessageState.Ready : MessageState.Leased,
            expires ? QueueAccountingFaultProtocol.Zero : QueueAccountingFaultProtocol.One,
            expires ? QueueAccountingFaultProtocol.One : QueueAccountingFaultProtocol.Two,
            scheduled ? QueueAccountingFaultProtocol.Zero : QueueAccountingFaultProtocol.One,
            message.NotBefore, message.ExpiresAt, LeaseOwner: expires ? null : QueueAccountingFaultProtocol.Root,
            LeaseVersion: expires ? QueueAccountingFaultProtocol.Zero : QueueAccountingFaultProtocol.One,
            LeaseUntil: expires ? null : start.AddSeconds(QueueAccountingFaultProtocol.LeaseSeconds)));
        var bodyBytes = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(
            QueueAccountingFaultProtocol.BodySpace, lane.Partition, lane.Queue, message.MessageId))!.LongLength);
        await Assert.That(Counters(fixture.Store, lane)).IsEqualTo(new QueueCounters(QueueAccountingFaultProtocol.One,
            bodyBytes, expires ? QueueAccountingFaultProtocol.Zero : QueueAccountingFaultProtocol.One,
            expires ? QueueAccountingFaultProtocol.Zero : bodyBytes,
            scheduled ? QueueAccountingFaultProtocol.Zero : QueueAccountingFaultProtocol.One));
        return new(lane, start, message, producer, produced, operation, kind, Counters(fixture.Store, lane), metadata, bodyBytes);
    }

    internal static QueueCounters Counters(ZoneTreeStore store, QueueLaneRef lane) => store.Read(view =>
        view.GetRecord<QueueCounters>(KeySpace.Partition(QueueAccountingFaultProtocol.CounterSpace, lane.Partition, lane.Queue))!);

    internal static void SetCounters(ZoneTreeStore store, QueueLaneRef lane, QueueCounters counters)
        => store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeySpace.Partition(QueueAccountingFaultProtocol.CounterSpace, lane.Partition, lane.Queue), counters);
            return true;
        });

    internal static QueueCounters Damage(QueueAccountingFaultState state, QueueAccountingColumn column) => column switch
    {
        QueueAccountingColumn.InFlightMessages => state.Counters with { InFlightMessages = QueueAccountingFaultProtocol.Zero },
        QueueAccountingColumn.InFlightBytes => state.Counters with { InFlightBytes = state.BodyBytes - QueueAccountingFaultProtocol.One },
        QueueAccountingColumn.StoredMessages => state.Counters with { StoredMessages = QueueAccountingFaultProtocol.Zero },
        QueueAccountingColumn.StoredBytes => state.Counters with { StoredBytes = state.BodyBytes - QueueAccountingFaultProtocol.One },
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    private static Command<DatabaseCommand> Operation(DatabaseEngine owner, QueueLaneRef lane,
        QueueAccountingOperation kind, string? token, DateTimeOffset time)
    {
        var id = Guid.NewGuid();
        if (kind == QueueAccountingOperation.Processing)
        {
            return owner.CreateCommand(OperationKind.Processing, id, QueueAccountingFaultProtocol.Root, time,
                JsonSerializer.Serialize(new ProcessingRequest(id, lane, token!, QueueAccountingFaultProtocol.Handler,
                    QueueAccountingFaultProtocol.One, [new PutDocument(QueueAccountingFaultProtocol.Collection,
                        QueueAccountingFaultProtocol.Document, QueueAccountingFaultProtocol.Payload)]), JsonDefaults.Options));
        }
        if (kind is QueueAccountingOperation.Ack or QueueAccountingOperation.Nack)
        {
            return owner.CreateCommand(OperationKind.Delivery, id, QueueAccountingFaultProtocol.Root, time,
                JsonSerializer.Serialize(new DeliveryCommand(id, lane, token!, kind == QueueAccountingOperation.Ack
                    ? DeliveryAction.Ack : DeliveryAction.Nack), JsonDefaults.Options));
        }
        return owner.CreateCommand(OperationKind.Receive, id, QueueAccountingFaultProtocol.Root, time,
            JsonSerializer.Serialize(new ReceiveRequest(id, lane, LeaseSeconds: QueueAccountingFaultProtocol.LeaseSeconds), JsonDefaults.Options));
    }
}
