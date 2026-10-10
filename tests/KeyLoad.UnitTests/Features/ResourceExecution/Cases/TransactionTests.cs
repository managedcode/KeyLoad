using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TransactionTests
{
    [Test]
    public async Task DocumentEventAndQueueCommitTogetherAndCommandRetryDoesNotRepeatEffects()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        db.Configure("events", ResourceKind.StreamSet);
        db.Configure("jobs", ResourceKind.WorkQueue);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [new PutDocument("orders", "o1", "{\"number\":1}", 0),
            new AppendEvents("events", "o1", [new("e1", "Created", "{\"number\":1}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage("jobs", "m1", "{\"order\":\"o1\"}")]);
        var first = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var retry = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        await Assert.That(retry.Token).IsEqualTo(first.Token);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision).IsEqualTo(1);
        await Assert.That(db.Database.ReadStream("root", new StreamRef(db.Partition, "events", "o1")).Events).HasSingleItem();
        await Assert.That(db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m1")!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(db.Submit(OperationKind.Batch, command with { Mutations = [new PutDocument("orders", "o2", "{}")] }, id: id).Error).IsEqualTo(ErrorCode.Conflict);
    }
    [Test]
    public async Task UniqueConflictRollsBackDocumentIndexEventAndEnqueue()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        db.Configure("events", ResourceKind.StreamSet);
        db.Configure("jobs", ResourceKind.WorkQueue);
        db.Commit(new PutDocument("orders", "existing", "{\"number\":1}"));
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new AppendEvents("events", "o2", [new("e2", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage("jobs", "m2", "{}"), new PutDocument("orders", "o2", "{\"number\":1}")]), id: id);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o2"))).IsNull();
        await Assert.That(db.Database.ReadStream("root", new StreamRef(db.Partition, "events", "o2")).Events).IsEmpty();
        await Assert.That(db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m2")).IsNull();
        await Assert.That(new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()).Execute("root", new(db.Partition, "SELECT * FROM orders WHERE number = 1")).Rows).HasSingleItem();
    }
    [Test]
    public async Task ConcurrentCompareAndSwapHasOneWinner()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "o1", "{}"));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(number => Task.Run(() =>
        {
            var id = Guid.NewGuid();
            return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [new PutDocument("orders", "o1", $"{{\"winner\":{number}}}", 1)]), id: id);
        })));
        await Assert.That(results).HasSingleItem(r => r.Error is null);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision).IsEqualTo(2);
    }
    [Test]
    public async Task SameLiteralPartitionKeyCannotCrossTransactionDomains()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("other", ResourceKind.WorkQueue, domain: "another-domain");
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}"), new EnqueueMessage("other", "m1", "{}")]), id: id).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))).IsNull();
    }
}
