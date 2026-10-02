using System.Text;
using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueBodyAccountingTests
{
    private const string QueueName = "jobs";
    private const string MessageId = "message";
    private const string LargePayloadPrefix = "{\"text\":\"";
    private const string LargePayloadSuffix = "\"}";
    private const int UnicodeCharacters = 1_000;

    [Test]
    public async Task AcMp006_StoredBytesDriveExactStoredAndInFlightQuotas()
    {
        var message = new EnqueueMessage(QueueName, MessageId,
            LargePayloadPrefix + new string('界', UnicodeCharacters) + LargePayloadSuffix);
        using var baseline = new TestDatabase();
        baseline.Configure(QueueName, ResourceKind.WorkQueue);
        baseline.Commit(message);
        var storedBytes = BodyLength(baseline, MessageId);

        using var exact = new TestDatabase();
        exact.Configure(QueueName, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxStoredBytes = storedBytes,
            MaxInFlightBytes = storedBytes
        });
        exact.Commit(message);
        await Assert.That(Counters(exact).StoredBytes).IsEqualTo(storedBytes);
        var lane = new QueueLaneRef(exact.Partition, QueueName);
        var receiveId = Guid.NewGuid();
        var delivery = await Assert.That(exact.Submit(OperationKind.Receive,
            new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(Counters(exact).InFlightBytes).IsEqualTo(storedBytes);
        var ackId = Guid.NewGuid();
        exact.Submit(OperationKind.Delivery,
            new DeliveryCommand(ackId, lane, delivery.Token, DeliveryAction.Ack), id: ackId).Get<CommitReceipt>();
        await Assert.That(Counters(exact).StoredBytes).IsEqualTo(0);
        await Assert.That(Counters(exact).InFlightBytes).IsEqualTo(0);

        using var storedShort = new TestDatabase();
        storedShort.Configure(QueueName, ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredBytes = storedBytes - 1 });
        var rejectId = Guid.NewGuid();
        var rejected = storedShort.Submit(OperationKind.Batch,
            new CommandRequest(rejectId, storedShort.Partition, [message]), id: rejectId);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Counters(storedShort).StoredMessages).IsEqualTo(0);

        using var flightShort = new TestDatabase();
        flightShort.Configure(QueueName, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxStoredBytes = storedBytes,
            MaxInFlightBytes = storedBytes - 1
        });
        flightShort.Commit(message);
        var shortReceiveId = Guid.NewGuid();
        await Assert.That(flightShort.Submit(OperationKind.Receive,
            new ReceiveRequest(shortReceiveId, new(flightShort.Partition, QueueName)), id: shortReceiveId)
            .Get<ReceiveResult>().Deliveries).IsEmpty();
        await Assert.That(Counters(flightShort).StoredBytes).IsEqualTo(storedBytes);
        await Assert.That(Counters(flightShort).InFlightBytes).IsEqualTo(0);
    }

    [Test]
    public async Task AcMp006_ScheduledExpiryAndMultipleClaimsKeepExactCountersAndFifo()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
        [
            new EnqueueMessage(QueueName, "first", "{}"),
            new EnqueueMessage(QueueName, "scheduled", "{}", NotBefore: now.AddSeconds(2), ExpiresAt: now.AddMinutes(1)),
            new EnqueueMessage(QueueName, "expired", "{}", ExpiresAt: now.AddSeconds(1))
        ]), id: id, time: now).Get<CommitReceipt>();
        var firstBytes = BodyLength(db, "first");
        var scheduledBytes = BodyLength(db, "scheduled");
        var expiredBytes = BodyLength(db, "expired");
        await Assert.That(Counters(db).StoredBytes).IsEqualTo(firstBytes + scheduledBytes + expiredBytes);

        var lane = new QueueLaneRef(db.Partition, QueueName);
        var receiveId = Guid.NewGuid();
        var deliveries = db.Submit(OperationKind.Receive,
            new ReceiveRequest(receiveId, lane, MaxMessages: 2), id: receiveId, time: now.AddSeconds(3))
            .Get<ReceiveResult>().Deliveries;
        await Assert.That(deliveries.Select(delivery => delivery.Id))
            .IsEquivalentTo(new[] { "first", "scheduled" }, CollectionOrdering.Matching);
        await Assert.That(Counters(db).StoredMessages).IsEqualTo(2);
        await Assert.That(Counters(db).StoredBytes).IsEqualTo(firstBytes + scheduledBytes);
        await Assert.That(Counters(db).InFlightMessages).IsEqualTo(2);
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(firstBytes + scheduledBytes);

        var ackId = Guid.NewGuid();
        db.Submit(OperationKind.Delivery,
            new DeliveryCommand(ackId, lane, deliveries[0].Token, DeliveryAction.Ack),
            id: ackId, time: now.AddSeconds(4)).Get<CommitReceipt>();
        var nackId = Guid.NewGuid();
        db.Submit(OperationKind.Delivery,
            new DeliveryCommand(nackId, lane, deliveries[1].Token, DeliveryAction.Nack),
            id: nackId, time: now.AddSeconds(4)).Get<CommitReceipt>();
        await Assert.That(Counters(db).StoredBytes).IsEqualTo(scheduledBytes);
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(0);
    }

    [Test]
    public async Task AcMp006_MissingAndMalformedStoredBodiesStillRejectThenRecover()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        db.Commit(new EnqueueMessage(QueueName, MessageId, "{}"));
        var key = BodyKey(db, MessageId);
        var original = db.Store.Read(view => view.ReadOwnedValue(key)!);
        var lane = new QueueLaneRef(db.Partition, QueueName);
        db.Store.Commit((tx, _) => { tx.Delete(key); return true; });
        var missingId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(missingId, lane), id: missingId).Error).IsEqualTo(ErrorCode.Corruption);
        db.Store.Commit((tx, _) => { tx.Put(key, Encoding.UTF8.GetBytes("{")); return true; });
        var malformedId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(malformedId, lane), id: malformedId).Error).IsEqualTo(ErrorCode.Validation);
        db.Store.Commit((tx, _) => { tx.Put(key, original); return true; });
        var healthyId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(healthyId, lane), id: healthyId).Get<ReceiveResult>().Deliveries).HasSingleItem();
    }

    private static byte[] BodyKey(TestDatabase db, string id)
        => KeySpace.Partition("message-body", db.Partition, QueueName, id);

    private static long BodyLength(TestDatabase db, string id) => db.Store.Read(view =>
    {
        var length = -1;
        view.ReadValue(BodyKey(db, id), value => length = value.Length);
        return (long)length;
    });

    private static QueueCounters Counters(TestDatabase db) => db.Store.Read(view =>
        view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", db.Partition, QueueName))
        ?? new(0, 0, 0, 0, 0));
}
