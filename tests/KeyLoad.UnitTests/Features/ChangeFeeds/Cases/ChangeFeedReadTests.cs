using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

using static KeyLoad.UnitTests.Features.ChangeFeeds.ChangeFeedTestActions;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ChangeFeedReadTests
{
    private const string OtherPartitionKey = "other";
    [Test]
    public async Task OutboxQuotaRollsBackTheEntireProducerAndPurgeReleasesCapacity()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new DatabaseEngine(db.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxOutboxRecords = 1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        OperationResult Write(params Mutation[] mutations)
        {
            var id = Guid.NewGuid();
            return engine.Apply(new(id, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(),
                JsonSerializer.Serialize(new CommandRequest(id, db.Partition, [.. mutations]), JsonDefaults.Options)));
        }
        await Assert.That(Write(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(engine.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(0);
        await Assert.That(engine.GetDocument("root", new(db.Partition, "orders", "a"))).IsNull();
        Write(new PutDocument("orders", "a", "{}")).Get<CommitReceipt>();
        await Assert.That(Write(new PutDocument("orders", "b", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var purgeId = Guid.NewGuid();
        db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(purgeId, db.Partition, 1), id: purgeId).Get<OutboxHead>();
        Write(new PutDocument("orders", "b", "{}")).Get<CommitReceipt>();
        await Assert.That(engine.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(2);
    }
    [Test]
    public async Task FeedResumesAnEmptyTailProjectsPiiAndRetainsDeletionMetadata()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/secret", "pii")]);
        Reader(db);
        var empty = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", Start: ChangeFeedStart.Now));
        await Assert.That(empty.Changes).IsEmpty();
        db.Commit(new PutDocument("orders", "a", "{\"n\":1,\"secret\":\"CANARY\"}"));
        var created = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", empty.Cursor));
        await Assert.That(JsonSerializer.Serialize(created, JsonDefaults.Options)).DoesNotContain("CANARY");
        await Assert.That(created.Changes.Single().After!.Redacted).IsTrue();
        db.Commit(new PatchDocument("orders", "a", [new("/n", PatchKind.Set, "2")], 1), new DeleteDocument("orders", "a", 2));
        var next = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", created.Cursor, Limit: 1));
        await Assert.That(next.HasMore).IsTrue();
        await Assert.That(next.Changes.Single().Before!.Revision).IsEqualTo(1);
        await Assert.That(next.Changes.Single().After!.Revision).IsEqualTo(2);
        var deleted = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", next.Cursor));
        await Assert.That(deleted.Changes.Single().Deleted).IsTrue();
        await Assert.That(deleted.Changes.Single().After).IsNull();
        await Assert.That(deleted.Changes.Single().Revision).IsEqualTo(3);
    }
    [Test]
    public async Task FeedNeverReturnsFormerOwnersDataAndAclChangesFenceOldCursors()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        Reader(db, owner: "alice");
        db.Commit(new PutDocument("orders", "a", "{\"secret\":\"ALICE\"}", Access: new("alice")));
        var first = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders"));
        await Assert.That(first.Changes).HasSingleItem();
        db.Commit(new PutDocument("orders", "a", "{\"secret\":\"BOB\"}", 1, new("bob"), true));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", first.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(db.Database.ReadChangeFeed("reader", new(db.Partition, "orders")).Changes).IsEmpty();
        Reader(db, "bob-reader", "bob");
        var bob = db.Database.ReadChangeFeed("bob-reader", new(db.Partition, "orders"));
        var change = await Assert.That(bob.Changes).HasSingleItem();
        await Assert.That(change.Before).IsNull();
        await Assert.That(JsonSerializer.Serialize(bob, JsonDefaults.Options)).DoesNotContain("ALICE");
    }
    [Test]
    public async Task FeedRevocationHistoryLossAndScopeMismatchAreExplicit()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        Reader(db);
        var empty = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders"));
        db.Commit(new PutDocument("orders", "a", "{}"));
        var wrong = new PartitionRef("tenant", "database", "orders", OtherPartitionKey);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(wrong, "orders", empty.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 1), id: id).Get<OutboxHead>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", empty.Cursor))).Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        var current = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders"));
        Reader(db, epoch: 2);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", current.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant", [], []) { Revoked = true, PolicyEpoch = 3 })).Get<PrincipalRecord>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", current.Cursor))).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }
    [Test]
    public async Task PublicFeedRequiresItsGrantAndCannotReadSystemProjectionPayloads()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        Reader(db);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("ordinary", "tenant", [new("database", "orders", Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("ordinary", new(db.Partition, "orders"))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetOutboxStatus("reader", db.Partition)).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadProjectionBatch("reader", new(Consumer(db)))).Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
    [Test]
    public async Task OutboxAndConsumerReceiptsRecoverAfterReopenAndCompaction()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("projection", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"));
        var consumer = Consumer(db);
        Configure(db, consumer);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var original = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        db.Store.Compact();
        db.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        await Assert.That(engine.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint).IsEqualTo(1);
        await Assert.That(engine.ReadChangeFeed("root", new(db.Partition, "orders")).Changes).HasSingleItem();
        var id = Guid.NewGuid();
        var replay = engine.Apply(new(id, OperationKind.CommitProjectionBatch, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(new CommitProjectionBatchRequest(id, consumer, batch.Token, [new PutDocument("projection", "a", "{}", 0)]), JsonDefaults.Options))).Get<ProjectionBatchResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(original.Receipt.Token);
        await Assert.That(engine.GetDocument("root", new(db.Partition, "projection", "a"))!.Revision).IsEqualTo(1);
    }
}
