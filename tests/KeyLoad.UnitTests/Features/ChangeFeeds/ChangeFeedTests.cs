using TUnit.Assertions.Enums;

using static KeyLoad.UnitTests.Features.ChangeFeeds.ChangeFeedTestActions;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ChangeFeedTestActions
{
    internal static ProjectionConsumerRef Consumer(TestDatabase db, string name = "text-v1") => new(db.Partition, name);
    internal static ProjectionConsumerInfo Configure(TestDatabase db, ProjectionConsumerRef consumer, long? after = null)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(1, ["orders"], ["putDocument", "patchDocument", "deleteDocument"]), after), id: id).Get<ProjectionConsumerInfo>();
    }
    internal static ProjectionBatchResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), id: id).Get<ProjectionBatchResult>();
    }
    internal static void Reader(TestDatabase db, string id = "reader", string? owner = null, string[]? grants = null, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id, "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.ChangesRead | Capability.Query)], [.. (grants ?? [])])
        { OwnerId = owner, RestrictRows = owner is not null, PolicyEpoch = epoch })).Get<PrincipalRecord>();
}

internal sealed class ChangeFeedTests
{
    [Test]
    public async Task EveryCanonicalMutationAndItsCommitShareThePartitionOutbox()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("events", ResourceKind.StreamSet);
        var receipt = db.Commit(new PutDocument("orders", "a", "{\"n\":1}"), new PatchDocument("orders", "a", [new("/n", PatchKind.Set, "2")], 1),
            new AppendEvents("events", "a", [new("e1", "Changed", "{}")], ExpectedStreamRevision.NoStream));
        var consumer = new ProjectionConsumerRef(db.Partition, "all");
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer, new(1, [], [])), id: id).Get<ProjectionConsumerInfo>();
        var entries = db.Database.ReadProjectionBatch("root", new(consumer)).Entries;
        await Assert.That(entries.Select(entry => entry.Sequence)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(entries.Select(entry => entry.Ordinal)).IsEquivalentTo(new[] { 0, 1, 2 }, CollectionOrdering.Matching);
        foreach (var entry in entries)
        {
            await Assert.That(entry.Commit).IsEqualTo(receipt.Token);
        }

        await Assert.That(entries[0].Before).IsNull();
        await Assert.That(entries[1].Before!.Revision).IsEqualTo(1);
        await Assert.That(entries[1].After!.Revision).IsEqualTo(2);
        await Assert.That(entries[2].Mutation).IsTypeOf<AppendEvents>();
    }
    [Test]
    public async Task FailedBatchAndSameCommandRetryCannotPublishPartialOrDuplicateChanges()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, indexes: [new("n", ["/n"], true)]);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition, [new PutDocument("orders", "a", "{\"n\":1}")]);
        db.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        db.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        var failedId = Guid.NewGuid();
        var failed = db.Submit(OperationKind.Batch, new CommandRequest(failedId, db.Partition,
            [new PutDocument("orders", "b", "{\"n\":2}"), new PutDocument("orders", "c", "{\"n\":1}")]), id: failedId);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "b"))).IsNull();
    }
    [Test]
    public async Task FailedProjectionEffectsDoNotAdvanceTheCheckpointAndDuplicateDeliveryIsSafe()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("projection", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"));
        var consumer = Consumer(db);
        Configure(db, consumer);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var failedId = Guid.NewGuid();
        var failed = db.Submit(OperationKind.CommitProjectionBatch, new CommitProjectionBatchRequest(failedId, consumer, batch.Token,
            [new PutDocument("projection", "a", "{}", 99)]), id: failedId);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint).IsEqualTo(0);
        var completed = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        var replay = Complete(db, consumer, batch, new PutDocument("projection", "a", "{}", 0));
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(completed.Receipt.Token);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "projection", "a"))!.Revision).IsEqualTo(1);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(2);
        var conflictId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(conflictId, consumer, batch.Token, []), id: conflictId).Error).IsEqualTo(ErrorCode.Conflict);
    }
    [Test]
    public async Task RetentionPinsProtectUnprocessedAndRebuildHistory()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var active = Consumer(db);
        Configure(db, active);
        var rebuild = Consumer(db, "text-v2");
        Configure(db, rebuild);
        Complete(db, active, db.Database.ReadProjectionBatch("root", new(active)));
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 2), id: id).Error).IsEqualTo(ErrorCode.Conflict);
        var releaseId = Guid.NewGuid();
        db.Submit(OperationKind.ReleaseProjectionConsumer, new ReleaseProjectionConsumerRequest(releaseId, rebuild, 1), id: releaseId).Get<ProjectionConsumerInfo>();
        var purgeId = Guid.NewGuid();
        var head = db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(purgeId, db.Partition, 2, Limit: 1), id: purgeId).Get<OutboxHead>();
        await Assert.That(head.FirstAvailable).IsEqualTo(2);
        await Assert.That(head.StoredRecords).IsEqualTo(1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Configure(db, Consumer(db, "old-cut"), 0)).Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadProjectionBatch("root", new(rebuild))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }
    [Test]
    public async Task AReleasedGenerationRejectsOldBatchesAndCachedCommandReceipts()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"));
        var consumer = Consumer(db);
        Configure(db, consumer);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var id = Guid.NewGuid();
        var command = new CommitProjectionBatchRequest(id, consumer, batch.Token, []);
        db.Submit(OperationKind.CommitProjectionBatch, command, id: id).Get<ProjectionBatchResult>();
        var releaseId = Guid.NewGuid();
        db.Submit(OperationKind.ReleaseProjectionConsumer, new ReleaseProjectionConsumerRequest(releaseId, consumer, 1), id: releaseId).Get<ProjectionConsumerInfo>();
        await Assert.That(db.Submit(OperationKind.CommitProjectionBatch, command, id: id).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Configure(db, consumer)).Code).IsEqualTo(ErrorCode.Conflict);
    }
    [Test]
    public async Task FilteredProjectionPositionsAdvanceContiguouslyWithoutPublishingForeignResources()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("other", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("other", "foreign", "{}"));
        var consumer = Consumer(db);
        Configure(db, consumer);
        var first = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        Complete(db, consumer, first);
        var remaining = db.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(remaining.Entries).IsEmpty();
        await Assert.That(remaining.ThroughSequence).IsEqualTo(2);
        Complete(db, consumer, remaining);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint).IsEqualTo(2);
    }
    [Test]
    public async Task FeedAndProjectionByteBoundariesPreserveTheFirstUndeliveredPosition()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"data\":\"" + new string('x', 1_000) + "\"}"), new PutDocument("orders", "b", "{}"));
        var full = db.Database.ReadChangeFeed("root", new(db.Partition, "orders"));
        var size = JsonDefaults.Serialize(full.Changes[0]).Length;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadChangeFeed("root", new(db.Partition, "orders", MaxBytes: size - 1))).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var first = db.Database.ReadChangeFeed("root", new(db.Partition, "orders", MaxBytes: size));
        await Assert.That(System.Linq.Enumerable.Single(first.Changes).Reference.Id).IsEqualTo("a");
        await Assert.That(first.HasMore).IsTrue();
        await Assert.That(first.ThroughSequence).IsEqualTo(1);
        await Assert.That(System.Linq.Enumerable.Single(db.Database.ReadChangeFeed("root", new(db.Partition, "orders", first.Cursor)).Changes).Reference.Id).IsEqualTo("b");
        var consumer = Consumer(db);
        Configure(db, consumer);
        var all = db.Database.ReadProjectionBatch("root", new(consumer));
        var entrySize = JsonDefaults.Serialize(all.Entries[0]).Length;
        var batch = db.Database.ReadProjectionBatch("root", new(consumer, MaxBytes: entrySize));
        await Assert.That(batch.Entries).HasSingleItem();
        await Assert.That(batch.ThroughSequence).IsEqualTo(1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Complete(db, consumer, batch with { Token = "invalid" })).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint).IsEqualTo(0);
    }
}
