using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLeaseReuseTests
{
    private const string QueueName = "jobs";
    private const string Root = "root";
    private const string Worker = "worker";
    private const string Handler = "handler";
    private const string MessageId = "input";

    [Test]
    public async Task AcMp006_RenewForeignExpiredAndStaleTokensPreserveLeaseFencing()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue, queuePolicy: new() { MaxLeaseSeconds = 30 });
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Worker, "tenant",
            [new("database", QueueName, Capability.QueueAck | Capability.QueueRenew)], []))).Get<PrincipalRecord>();
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var enqueueId = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(enqueueId, db.Partition,
            [new EnqueueMessage(QueueName, MessageId, "{}")]), id: enqueueId, time: now).Get<CommitReceipt>();
        var lane = new QueueLaneRef(db.Partition, QueueName);
        var receiveId = Guid.NewGuid();
        var first = await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(receiveId, lane, LeaseSeconds: 1), id: receiveId, time: now)
            .Get<ReceiveResult>().Deliveries).HasSingleItem();
        var bodyBytes = BodyLength(db);
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(bodyBytes);

        var foreignId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Delivery,
            new DeliveryCommand(foreignId, lane, first.Token, DeliveryAction.Ack),
            principal: Worker, id: foreignId, time: now).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var renewId = Guid.NewGuid();
        db.Submit(OperationKind.Delivery,
            new DeliveryCommand(renewId, lane, first.Token, DeliveryAction.Renew, LeaseSeconds: 1),
            id: renewId, time: now.AddMilliseconds(500)).Get<CommitReceipt>();
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(bodyBytes);
        var expiredId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Delivery,
            new DeliveryCommand(expiredId, lane, first.Token, DeliveryAction.Ack),
            id: expiredId, time: now.AddSeconds(2)).Error).IsEqualTo(ErrorCode.LeaseExpired);

        var secondId = Guid.NewGuid();
        var second = await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(secondId, lane, LeaseSeconds: 10), id: secondId, time: now.AddSeconds(3))
            .Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(bodyBytes);
        var staleId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Delivery,
            new DeliveryCommand(staleId, lane, first.Token, DeliveryAction.Ack),
            id: staleId, time: now.AddSeconds(3)).Error).IsEqualTo(ErrorCode.StaleLease);
        var ackId = Guid.NewGuid();
        db.Submit(OperationKind.Delivery,
            new DeliveryCommand(ackId, lane, second.Token, DeliveryAction.Ack),
            id: ackId, time: now.AddSeconds(3)).Get<CommitReceipt>();
        await Assert.That(Counters(db).InFlightBytes).IsEqualTo(0);
        await Assert.That(Counters(db).StoredBytes).IsEqualTo(0);
        await Assert.That(db.Database.InspectMessage(Root, lane, MessageId)!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    [Test]
    public async Task AcMp006_ProcessingReusesLeaseAndReleasesQuotaBeforeEffects()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredMessages = 1 });
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new EnqueueMessage(QueueName, MessageId, "{}"));
        var lane = new QueueLaneRef(db.Partition, QueueName);
        var receiveId = Guid.NewGuid();
        var delivery = await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();

        var failedId = Guid.NewGuid();
        var failed = new ProcessingRequest(failedId, lane, delivery.Token, Handler, 1,
            [new EnqueueMessage(QueueName, "rolled-back", "{}"), new PutDocument("orders", "missing", "{}", 9)]);
        await Assert.That(db.Submit(OperationKind.Processing, failed, id: failedId).Error)
            .IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(db.Database.InspectMessage(Root, lane, MessageId)!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(db.Database.InspectMessage(Root, lane, "rolled-back")).IsNull();

        var successId = Guid.NewGuid();
        var success = new ProcessingRequest(successId, lane, delivery.Token, Handler, 1,
            [new EnqueueMessage(QueueName, "output", "{}")]);
        var receipt = db.Submit(OperationKind.Processing, success, id: successId).Get<CommitReceipt>();
        await Assert.That(db.Database.InspectMessage(Root, lane, MessageId)!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(db.Database.InspectMessage(Root, lane, "output")!.Metadata.State).IsEqualTo(MessageState.Ready);
        var replayId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Processing, success with { CommandId = replayId }, id: replayId)
            .Get<CommitReceipt>().Token).IsEqualTo(receipt.Token);
        var nextId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Receive,
            new ReceiveRequest(nextId, lane), id: nextId).Get<ReceiveResult>().Deliveries).HasSingleItem();
    }

    [Test]
    public async Task AcMp006_DirectResourceAndProcessingTokenErrorsKeepTheirOrder()
    {
        using var db = new TestDatabase();
        var lane = new QueueLaneRef(db.Partition, "unconfigured");
        var deliveryId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Delivery,
            new DeliveryCommand(deliveryId, lane, "invalid", DeliveryAction.Ack), id: deliveryId).Error)
            .IsEqualTo(ErrorCode.NotFound);
        var processingId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Processing,
            new ProcessingRequest(processingId, lane, "invalid", Handler, 1, []), id: processingId).Error)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static long BodyLength(TestDatabase db) => db.Store.Read(view =>
    {
        var length = -1;
        view.ReadValue(KeySpace.Partition("message-body", db.Partition, QueueName, MessageId), value => length = value.Length);
        return (long)length;
    });

    private static QueueCounters Counters(TestDatabase db) => db.Store.Read(view =>
        view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", db.Partition, QueueName))
        ?? new(0, 0, 0, 0, 0));
}
