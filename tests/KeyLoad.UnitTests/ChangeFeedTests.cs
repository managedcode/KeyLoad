using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class ChangeFeedTests
{
    private static ProjectionConsumerRef Consumer(TestDatabase db, string name = "text-v1") => new(db.Partition, name);
    private static ProjectionConsumerInfo Configure(TestDatabase db, ProjectionConsumerRef consumer, long? after = null)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(1, ["orders"], ["putDocument", "patchDocument", "deleteDocument"]), after), id: id).Get<ProjectionConsumerInfo>();
    }
    private static ProjectionBatchResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, effects), id: id).Get<ProjectionBatchResult>();
    }
    private static void Reader(TestDatabase db, string id = "reader", string? owner = null, string[]? grants = null, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id, "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.ChangesRead | Capability.Query)], grants ?? [])
            { OwnerId = owner, RestrictRows = owner is not null, PolicyEpoch = epoch })).Get<PrincipalRecord>();
    [Fact]
    public void EveryCanonicalMutationAndItsCommitShareThePartitionOutbox()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("events", ResourceKind.StreamSet);
        var receipt = db.Commit(new PutDocument("orders", "a", "{\"n\":1}"), new PatchDocument("orders", "a", [new("/n", PatchKind.Set, "2")], 1),
            new AppendEvents("events", "a", [new("e1", "Changed", "{}")], ExpectedStreamRevision.NoStream));
        var consumer = new ProjectionConsumerRef(db.Partition, "all"); var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer, new(1, [], [])), id: id).Get<ProjectionConsumerInfo>();
        var entries = db.Database.ReadProjectionBatch("root", new(consumer)).Entries;
        Assert.Equal(new long[] { 1, 2, 3 }, entries.Select(entry => entry.Sequence));
        Assert.Equal(new[] { 0, 1, 2 }, entries.Select(entry => entry.Ordinal));
        Assert.All(entries, entry => Assert.Equal(receipt.Token, entry.Commit));
        Assert.Null(entries[0].Before); Assert.Equal(1, entries[1].Before!.Revision); Assert.Equal(2, entries[1].After!.Revision);
        Assert.IsType<AppendEvents>(entries[2].Mutation);
    }
    [Fact]
    public void FailedBatchAndSameCommandRetryCannotPublishPartialOrDuplicateChanges()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, indexes: [new("n", ["/n"], true)]);
        var id = Guid.NewGuid(); var request = new CommandRequest(id, db.Partition, [new PutDocument("orders", "a", "{\"n\":1}")]);
        db.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>(); db.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        var failedId = Guid.NewGuid(); var failed = db.Submit(OperationKind.Batch, new CommandRequest(failedId, db.Partition,
            [new PutDocument("orders", "b", "{\"n\":2}"), new PutDocument("orders", "c", "{\"n\":1}")]), id: failedId);
        Assert.Equal(ErrorCode.Conflict, failed.Error);
        Assert.Equal(1, db.Database.GetOutboxStatus("root", db.Partition).Head.Tail);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "b")));
    }
    [Fact]
    public void FailedProjectionEffectsDoNotAdvanceTheCheckpointAndDuplicateDeliveryIsSafe()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("projection", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}")); var consumer = Consumer(db); Configure(db, consumer);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer)); var failedId = Guid.NewGuid();
        var failed = db.Submit(OperationKind.CommitProjectionBatch, new CommitProjectionBatchRequest(failedId, consumer, batch.Token,
            [new PutDocument("projection", "a", "{}", 99)]), id: failedId);
        Assert.Equal(ErrorCode.RevisionConflict, failed.Error); Assert.Equal(0, db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint);
        var completed = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        var replay = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        Assert.True(replay.AlreadyProcessed); Assert.Equal(completed.Receipt.Token, replay.Receipt.Token);
        Assert.Equal(1, db.Database.GetDocument("root", new(db.Partition, "projection", "a"))!.Revision);
        Assert.Equal(2, db.Database.GetOutboxStatus("root", db.Partition).Head.Tail);
        var conflictId = Guid.NewGuid();
        Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(conflictId, consumer, batch.Token, []), id: conflictId).Error);
    }
    [Fact]
    public void RetentionPinsProtectUnprocessedAndRebuildHistory()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var active = Consumer(db); Configure(db, active); var rebuild = Consumer(db, "text-v2"); Configure(db, rebuild);
        Complete(db, active, db.Database.ReadProjectionBatch("root", new(active)));
        var id = Guid.NewGuid(); Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 2), id: id).Error);
        var releaseId = Guid.NewGuid(); db.Submit(OperationKind.ReleaseProjectionConsumer, new ReleaseProjectionConsumerRequest(releaseId, rebuild, 1), id: releaseId).Get<ProjectionConsumerInfo>();
        var purgeId = Guid.NewGuid(); var head = db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(purgeId, db.Partition, 2, Limit: 1), id: purgeId).Get<OutboxHead>();
        Assert.Equal(2, head.FirstAvailable); Assert.Equal(1, head.StoredRecords);
        Assert.Equal(ErrorCode.HistoryUnavailable, Assert.Throws<KeyLoadException>(() => Configure(db, Consumer(db, "old-cut"), 0)).Code);
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.ReadProjectionBatch("root", new(rebuild))).Code);
    }
    [Fact]
    public void AReleasedGenerationRejectsOldBatchesAndCachedCommandReceipts()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Commit(new PutDocument("orders", "a", "{}"));
        var consumer = Consumer(db); Configure(db, consumer); var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var id = Guid.NewGuid(); var command = new CommitProjectionBatchRequest(id, consumer, batch.Token, []);
        db.Submit(OperationKind.CommitProjectionBatch, command, id: id).Get<ProjectionBatchResult>();
        var releaseId = Guid.NewGuid(); db.Submit(OperationKind.ReleaseProjectionConsumer, new ReleaseProjectionConsumerRequest(releaseId, consumer, 1), id: releaseId).Get<ProjectionConsumerInfo>();
        Assert.Equal(ErrorCode.TokenInvalidated, db.Submit(OperationKind.CommitProjectionBatch, command, id: id).Error);
        Assert.Equal(ErrorCode.Conflict, Assert.Throws<KeyLoadException>(() => Configure(db, consumer)).Code);
    }
    [Fact]
    public void FilteredProjectionPositionsAdvanceContiguouslyWithoutPublishingForeignResources()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("other", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("other", "foreign", "{}"));
        var consumer = Consumer(db); Configure(db, consumer); var first = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        Complete(db, consumer, first); var remaining = db.Database.ReadProjectionBatch("root", new(consumer));
        Assert.Empty(remaining.Entries); Assert.Equal(2, remaining.ThroughSequence);
        Complete(db, consumer, remaining); Assert.Equal(2, db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint);
    }
    [Fact]
    public void FeedAndProjectionByteBoundariesPreserveTheFirstUndeliveredPosition()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"data\":\"" + new string('x', 1_000) + "\"}"), new PutDocument("orders", "b", "{}"));
        var full = db.Database.ReadChangeFeed("root", new(db.Partition, "orders")); var size = JsonDefaults.Serialize(full.Changes[0]).Length;
        Assert.Equal(ErrorCode.BudgetExceeded, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("root", new(db.Partition, "orders", MaxBytes: size - 1))).Code);
        var first = db.Database.ReadChangeFeed("root", new(db.Partition, "orders", MaxBytes: size));
        Assert.Equal("a", Assert.Single(first.Changes).Reference.Id); Assert.True(first.HasMore); Assert.Equal(1, first.ThroughSequence);
        Assert.Equal("b", Assert.Single(db.Database.ReadChangeFeed("root", new(db.Partition, "orders", first.Cursor)).Changes).Reference.Id);
        var consumer = Consumer(db); Configure(db, consumer); var all = db.Database.ReadProjectionBatch("root", new(consumer));
        var entrySize = JsonDefaults.Serialize(all.Entries[0]).Length;
        var batch = db.Database.ReadProjectionBatch("root", new(consumer, MaxBytes: entrySize)); Assert.Single(batch.Entries); Assert.Equal(1, batch.ThroughSequence);
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => Complete(db, consumer, batch with { Token = "invalid" })).Code);
        Assert.Equal(0, db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint);
    }
    [Fact]
    public void OutboxQuotaRollsBackTheEntireProducerAndPurgeReleasesCapacity()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        var engine = new DatabaseEngine(db.Store, new AuthorizationPolicy(), new() { MaxOutboxRecords = 1 });
        OperationResult Write(params Mutation[] mutations)
        {
            var id = Guid.NewGuid(); return engine.Apply(new(id, OperationKind.Batch, "root", DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(new CommandRequest(id, db.Partition, mutations), JsonDefaults.Options)));
        }
        Assert.Equal(ErrorCode.ResourceExhausted, Write(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}")).Error);
        Assert.Equal(0, engine.GetOutboxStatus("root", db.Partition).Head.Tail); Assert.Null(engine.GetDocument("root", new(db.Partition, "orders", "a")));
        Write(new PutDocument("orders", "a", "{}")).Get<CommitReceipt>(); Assert.Equal(ErrorCode.ResourceExhausted, Write(new PutDocument("orders", "b", "{}")).Error);
        var purgeId = Guid.NewGuid(); db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(purgeId, db.Partition, 1), id: purgeId).Get<OutboxHead>();
        Write(new PutDocument("orders", "b", "{}")).Get<CommitReceipt>(); Assert.Equal(2, engine.GetOutboxStatus("root", db.Partition).Head.Tail);
    }
    [Fact]
    public void FeedResumesAnEmptyTailProjectsPiiAndRetainsDeletionMetadata()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, fields: [new("/secret", "pii")]); Reader(db);
        var empty = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", Start: ChangeFeedStart.Now)); Assert.Empty(empty.Changes);
        db.Commit(new PutDocument("orders", "a", "{\"n\":1,\"secret\":\"CANARY\"}"));
        var created = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", empty.Cursor));
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(created, JsonDefaults.Options)); Assert.True(created.Changes.Single().After!.Redacted);
        db.Commit(new PatchDocument("orders", "a", [new("/n", PatchKind.Set, "2")], 1), new DeleteDocument("orders", "a", 2));
        var next = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", created.Cursor, Limit: 1));
        Assert.True(next.HasMore); Assert.Equal(1, next.Changes.Single().Before!.Revision); Assert.Equal(2, next.Changes.Single().After!.Revision);
        var deleted = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", next.Cursor));
        Assert.True(deleted.Changes.Single().Deleted); Assert.Null(deleted.Changes.Single().After); Assert.Equal(3, deleted.Changes.Single().Revision);
    }
    [Fact]
    public void FeedNeverReturnsFormerOwnersDataAndAclChangesFenceOldCursors()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); Reader(db, owner: "alice");
        db.Commit(new PutDocument("orders", "a", "{\"secret\":\"ALICE\"}", Access: new("alice")));
        var first = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders")); Assert.Single(first.Changes);
        db.Commit(new PutDocument("orders", "a", "{\"secret\":\"BOB\"}", 1, new("bob"), true));
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", first.Cursor))).Code);
        Assert.Empty(db.Database.ReadChangeFeed("reader", new(db.Partition, "orders")).Changes);
        Reader(db, "bob-reader", "bob"); var bob = db.Database.ReadChangeFeed("bob-reader", new(db.Partition, "orders"));
        var change = Assert.Single(bob.Changes); Assert.Null(change.Before); Assert.DoesNotContain("ALICE", JsonSerializer.Serialize(bob, JsonDefaults.Options));
    }
    [Fact]
    public void FeedRevocationHistoryLossAndScopeMismatchAreExplicit()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); Reader(db);
        var empty = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders")); db.Commit(new PutDocument("orders", "a", "{}"));
        var wrong = new PartitionRef("tenant", "database", "orders", "other");
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(wrong, "orders", empty.Cursor))).Code);
        var id = Guid.NewGuid(); db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 1), id: id).Get<OutboxHead>();
        Assert.Equal(ErrorCode.HistoryUnavailable, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", empty.Cursor))).Code);
        var current = db.Database.ReadChangeFeed("reader", new(db.Partition, "orders")); Reader(db, epoch: 2);
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", current.Cursor))).Code);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant", [], []) { Revoked = true, PolicyEpoch = 3 })).Get<PrincipalRecord>();
        Assert.Equal(ErrorCode.Unauthenticated, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("reader", new(db.Partition, "orders", current.Cursor))).Code);
    }
    [Fact]
    public void PublicFeedRequiresItsGrantAndCannotReadSystemProjectionPayloads()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); Reader(db);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("ordinary", "tenant", [new("database", "orders", Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => db.Database.ReadChangeFeed("ordinary", new(db.Partition, "orders"))).Code);
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => db.Database.GetOutboxStatus("reader", db.Partition)).Code);
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => db.Database.ReadProjectionBatch("reader", new(Consumer(db)))).Code);
    }
    [Fact]
    public void OutboxAndConsumerReceiptsRecoverAfterReopenAndCompaction()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("projection", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}")); var consumer = Consumer(db); Configure(db, consumer);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer)); var original = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        db.Store.Compact(); db.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(db.Directory)); var engine = new DatabaseEngine(reopened, new AuthorizationPolicy());
        Assert.Equal(1, engine.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint);
        Assert.Single(engine.ReadChangeFeed("root", new(db.Partition, "orders")).Changes);
        var id = Guid.NewGuid(); var replay = engine.Apply(new(id, OperationKind.CommitProjectionBatch, "root", DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new CommitProjectionBatchRequest(id, consumer, batch.Token, [new PutDocument("projection", "a", "{}", 0)]), JsonDefaults.Options))).Get<ProjectionBatchResult>();
        Assert.True(replay.AlreadyProcessed); Assert.Equal(original.Receipt.Token, replay.Receipt.Token);
        Assert.Equal(1, engine.GetDocument("root", new(db.Partition, "projection", "a"))!.Revision);
    }
}
