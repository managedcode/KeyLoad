using KeyLoad.Query;

namespace KeyLoad.UnitTests;

public sealed class TransactionTests
{
    [Fact]
    public void DocumentEventAndQueueCommitTogetherAndCommandRetryDoesNotRepeatEffects()
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
        Assert.Equal(first.Token, retry.Token);
        Assert.Equal(1, db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision);
        Assert.Single(db.Database.ReadStream("root", new(db.Partition, "events", "o1")).Events);
        Assert.Equal(MessageState.Ready, db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m1")!.Metadata.State);
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch, command with { Mutations = [new PutDocument("orders", "o2", "{}") ] }, id: id).Error);
    }
    [Fact]
    public void UniqueConflictRollsBackDocumentIndexEventAndEnqueue()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        db.Configure("events", ResourceKind.StreamSet); db.Configure("jobs", ResourceKind.WorkQueue);
        db.Commit(new PutDocument("orders", "existing", "{\"number\":1}"));
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new AppendEvents("events", "o2", [new("e2", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage("jobs", "m2", "{}"), new PutDocument("orders", "o2", "{\"number\":1}")]), id: id);
        Assert.Equal(ErrorCode.Conflict, result.Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o2")));
        Assert.Empty(db.Database.ReadStream("root", new(db.Partition, "events", "o2")).Events);
        Assert.Null(db.Database.InspectMessage("root", new(db.Partition, "jobs"), "m2"));
        Assert.Single(new QueryEngine(db.Database).Execute("root", new(db.Partition, "SELECT * FROM orders WHERE number = 1")).Rows);
    }
    [Fact]
    public async Task ConcurrentCompareAndSwapHasOneWinner()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "o1", "{}"));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(number => Task.Run(() =>
        {
            var id = Guid.NewGuid();
            return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [new PutDocument("orders", "o1", $"{{\"winner\":{number}}}", 1)]), id: id);
        })));
        Assert.Single(results, r => r.Error is null);
        Assert.Equal(2, db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision);
    }
    [Fact]
    public void SameLiteralPartitionKeyCannotCrossTransactionDomains()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("other", ResourceKind.WorkQueue, domain: "another-domain");
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}"), new EnqueueMessage("other", "m1", "{}")]), id: id).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o1")));
    }
}
