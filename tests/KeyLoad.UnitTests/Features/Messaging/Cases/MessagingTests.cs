namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class MessagingTests
{
    [Test]
    public async Task InboxEffectsAndAckAreAtomicAndReplayCannotApplyDifferentEffects()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("jobs", ResourceKind.WorkQueue);
        db.Commit(new EnqueueMessage("jobs", "m1", "{}"));
        var receiveId = Guid.NewGuid();
        var lane = new QueueLaneRef(db.Partition, "jobs");
        var delivery = await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        var id = Guid.NewGuid();
        var process = new ProcessingRequest(id, lane, delivery.Token, "worker", 1, [new PutDocument("orders", "o1", "{}", 0)]);
        var receipt = db.Submit(OperationKind.Processing, process, id: id).Get<CommitReceipt>();
        await Assert.That(db.Database.InspectMessage("root", lane, "m1")!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(db.Submit(OperationKind.Processing, process, id: id).Get<CommitReceipt>().Token).IsEqualTo(receipt.Token);
        var secondId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Processing, process with { CommandId = secondId }, id: secondId).Get<CommitReceipt>().Token).IsEqualTo(receipt.Token);
        var changedId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Processing, process with { CommandId = changedId, Effects = [new PutDocument("orders", "o2", "{}", 0)] }, id: changedId).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o2"))).IsNull();
    }
    [Test]
    public async Task ExpiredWorkerCannotAckAfterReclaimAndAttemptsReachDeadLetter()
    {
        using var db = new TestDatabase();
        db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxAttempts = 2, MaxLeaseSeconds = 1 });
        db.Commit(new EnqueueMessage("jobs", "m1", "{}"));
        var now = TimeProvider.System.GetUtcNow();
        var lane = new QueueLaneRef(db.Partition, "jobs");
        var r1 = Guid.NewGuid();
        var first = await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(r1, lane, LeaseSeconds: 1), id: r1, time: now).Get<ReceiveResult>().Deliveries).HasSingleItem();
        var r2 = Guid.NewGuid();
        var second = await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(r2, lane, LeaseSeconds: 1), id: r2, time: now.AddSeconds(2)).Get<ReceiveResult>().Deliveries).HasSingleItem();
        var ackId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Delivery, new DeliveryCommand(ackId, lane, first.Token, DeliveryAction.Ack), id: ackId, time: now.AddSeconds(2)).Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(second.LeaseVersion).IsEqualTo(first.LeaseVersion + 1);
        var r3 = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(r3, lane, LeaseSeconds: 1), id: r3, time: now.AddSeconds(4)).Get<ReceiveResult>().Deliveries).IsEmpty();
        await Assert.That(db.Database.InspectMessage("root", lane, "m1")!.Metadata.State).IsEqualTo(MessageState.DeadLettered);
        await Assert.That(db.Database.InspectMessage("root", lane, "m1")!.PayloadJson).IsEqualTo("{}");
    }
    [Test]
    public async Task QueueQuotaFailureRollsBackProducerDocument()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredMessages = 1 });
        db.Commit(new EnqueueMessage("jobs", "first", "{}"));
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}"), new EnqueueMessage("jobs", "second", "{}")]), id: id).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))).IsNull();
    }
    [Test]
    public async Task ProcessingReleasesInputQuotaAtomicallyAndFailureRestoresItsLease()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredMessages = 1 });
        db.Commit(new EnqueueMessage("jobs", "input", "{}"));
        var lane = new QueueLaneRef(db.Partition, "jobs");
        var receiveId = Guid.NewGuid();
        var delivery = await Assert.That(db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        var failedId = Guid.NewGuid();
        var failed = db.Submit(OperationKind.Processing, new ProcessingRequest(failedId, lane, delivery.Token, "worker", 1,
            [new EnqueueMessage("jobs", "rolled-back", "{}"), new PutDocument("orders", "missing", "{}", 9)]), id: failedId);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(db.Database.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(db.Database.InspectMessage("root", lane, "rolled-back")).IsNull();
        var id = Guid.NewGuid();
        db.Submit(OperationKind.Processing, new ProcessingRequest(id, lane, delivery.Token, "worker", 1,
            [new EnqueueMessage("jobs", "replacement", "{}")]), id: id).Get<CommitReceipt>();
        await Assert.That(db.Database.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(db.Database.InspectMessage("root", lane, "replacement")!.Metadata.State).IsEqualTo(MessageState.Ready);
    }
}
