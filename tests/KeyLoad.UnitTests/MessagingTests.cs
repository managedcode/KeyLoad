namespace KeyLoad.UnitTests;

public sealed class MessagingTests
{
    [Fact]
    public void InboxEffectsAndAckAreAtomicAndReplayCannotApplyDifferentEffects()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("jobs", ResourceKind.WorkQueue);
        db.Commit(new EnqueueMessage("jobs", "m1", "{}"));
        var receiveId = Guid.NewGuid(); var lane = new QueueLaneRef(db.Partition, "jobs");
        var delivery = Assert.Single(db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries);
        var id = Guid.NewGuid(); var process = new ProcessingRequest(id, lane, delivery.Token, "worker", 1, [new PutDocument("orders", "o1", "{}", 0)]);
        var receipt = db.Submit(OperationKind.Processing, process, id: id).Get<CommitReceipt>();
        Assert.Equal(MessageState.Acked, db.Database.InspectMessage("root", lane, "m1")!.Metadata.State);
        Assert.Equal(receipt.Token, db.Submit(OperationKind.Processing, process, id: id).Get<CommitReceipt>().Token);
        var secondId = Guid.NewGuid();
        Assert.Equal(receipt.Token, db.Submit(OperationKind.Processing, process with { CommandId = secondId }, id: secondId).Get<CommitReceipt>().Token);
        var changedId = Guid.NewGuid();
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Processing, process with { CommandId = changedId, Effects = [new PutDocument("orders", "o2", "{}", 0)] }, id: changedId).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o2")));
    }
    [Fact]
    public void ExpiredWorkerCannotAckAfterReclaimAndAttemptsReachDeadLetter()
    {
        using var db = new TestDatabase(); db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxAttempts = 2, MaxLeaseSeconds = 1 });
        db.Commit(new EnqueueMessage("jobs", "m1", "{}"));
        var now = DateTimeOffset.UtcNow; var lane = new QueueLaneRef(db.Partition, "jobs");
        var r1 = Guid.NewGuid(); var first = Assert.Single(db.Submit(OperationKind.Receive, new ReceiveRequest(r1, lane, LeaseSeconds: 1), id: r1, time: now).Get<ReceiveResult>().Deliveries);
        var r2 = Guid.NewGuid(); var second = Assert.Single(db.Submit(OperationKind.Receive, new ReceiveRequest(r2, lane, LeaseSeconds: 1), id: r2, time: now.AddSeconds(2)).Get<ReceiveResult>().Deliveries);
        var ackId = Guid.NewGuid();
        Assert.Equal(ErrorCode.StaleLease, db.Submit(OperationKind.Delivery, new DeliveryCommand(ackId, lane, first.Token, DeliveryAction.Ack), id: ackId, time: now.AddSeconds(2)).Error);
        Assert.Equal(first.LeaseVersion + 1, second.LeaseVersion);
        var r3 = Guid.NewGuid(); Assert.Empty(db.Submit(OperationKind.Receive, new ReceiveRequest(r3, lane, LeaseSeconds: 1), id: r3, time: now.AddSeconds(4)).Get<ReceiveResult>().Deliveries);
        Assert.Equal(MessageState.DeadLettered, db.Database.InspectMessage("root", lane, "m1")!.Metadata.State);
        Assert.Equal("{}", db.Database.InspectMessage("root", lane, "m1")!.PayloadJson);
    }
    [Fact]
    public void QueueQuotaFailureRollsBackProducerDocument()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredMessages = 1 });
        db.Commit(new EnqueueMessage("jobs", "first", "{}"));
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.ResourceExhausted, db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}"), new EnqueueMessage("jobs", "second", "{}")]), id: id).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o1")));
    }
    [Fact]
    public void ProcessingReleasesInputQuotaAtomicallyAndFailureRestoresItsLease()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Configure("jobs", ResourceKind.WorkQueue, queuePolicy: new() { MaxStoredMessages = 1 });
        db.Commit(new EnqueueMessage("jobs", "input", "{}"));
        var lane = new QueueLaneRef(db.Partition, "jobs"); var receiveId = Guid.NewGuid();
        var delivery = Assert.Single(db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries);
        var failedId = Guid.NewGuid();
        var failed = db.Submit(OperationKind.Processing, new ProcessingRequest(failedId, lane, delivery.Token, "worker", 1,
            [new EnqueueMessage("jobs", "rolled-back", "{}"), new PutDocument("orders", "missing", "{}", 9)]), id: failedId);
        Assert.Equal(ErrorCode.RevisionConflict, failed.Error);
        Assert.Equal(MessageState.Leased, db.Database.InspectMessage("root", lane, "input")!.Metadata.State);
        Assert.Null(db.Database.InspectMessage("root", lane, "rolled-back"));
        var id = Guid.NewGuid();
        db.Submit(OperationKind.Processing, new ProcessingRequest(id, lane, delivery.Token, "worker", 1,
            [new EnqueueMessage("jobs", "replacement", "{}")]), id: id).Get<CommitReceipt>();
        Assert.Equal(MessageState.Acked, db.Database.InspectMessage("root", lane, "input")!.Metadata.State);
        Assert.Equal(MessageState.Ready, db.Database.InspectMessage("root", lane, "replacement")!.Metadata.State);
    }
}
