using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class ProjectionProgressTests
{
    private static ProjectionConsumerRef Configure(TestDatabase db, string name = "projection-v1")
    {
        var consumer = new ProjectionConsumerRef(db.Partition, name); var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer,
            new(1, ["orders"], ["putDocument"])), id: id).Get<ProjectionConsumerInfo>();
        return consumer;
    }
    private static OperationResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, effects), id: id);
    }
    private static OperationResult Write(TestDatabase db, params Mutation[] effects)
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, effects), id: id);
    }
    private static OutboxHead Purge(TestDatabase db, long through)
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, through), id: id).Get<OutboxHead>();
    }
    [Fact]
    public void AFullOutboxAllowsProjectionProgressButStillStopsOrdinaryProducers()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 1 });
        db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}")); var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var completed = Complete(db, consumer, batch, new PutDocument("derived", "effect", "{}", 0)).Get<ProjectionBatchResult>();
        Assert.Equal(1, completed.Checkpoint);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.Equal(2, status.Head.StoredRecords); Assert.Equal(1, status.Consumers.Single().LastProgressReservationCut);
        Assert.Equal(ErrorCode.ResourceExhausted, Write(db, new PutDocument("orders", "blocked", "{}")).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "blocked")));
        Assert.True(Complete(db, consumer, batch, new PutDocument("derived", "effect", "{}", 0)).Get<ProjectionBatchResult>().AlreadyProcessed);
        Assert.Equal(2, db.Database.GetOutboxStatus("root", db.Partition).Head.Tail);
        // A filtered batch can advance without spending reserve, allowing the derived entries to be reclaimed too.
        var filtered = db.Database.ReadProjectionBatch("root", new(consumer)); Assert.Empty(filtered.Entries);
        Complete(db, consumer, filtered).Get<ProjectionBatchResult>(); Assert.Equal(0, Purge(db, 2).StoredRecords);
        Write(db, new PutDocument("orders", "next", "{}")).Get<CommitReceipt>();
    }
    [Fact]
    public void EachPinnedConsumerCanAdvanceIndependentlyAndCleanupUnblocksThePartition()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 2 });
        db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        var first = Configure(db, "first"); var second = Configure(db, "second");
        Complete(db, first, db.Database.ReadProjectionBatch("root", new(first)), new PutDocument("derived", "first", "{}")).Get<ProjectionBatchResult>();
        Complete(db, second, db.Database.ReadProjectionBatch("root", new(second, Limit: 1)), new PutDocument("derived", "second", "{}")).Get<ProjectionBatchResult>();
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.Equal(3, status.Head.StoredRecords); Assert.All(status.Consumers, consumer => Assert.Equal(1, consumer.Checkpoint));
        Assert.Equal(2, Purge(db, 1).StoredRecords);
        foreach (var consumer in new[] { first, second })
            Complete(db, consumer, db.Database.ReadProjectionBatch("root", new(consumer))).Get<ProjectionBatchResult>();
        Assert.All(db.Database.GetOutboxStatus("root", db.Partition).Consumers,
            consumer => Assert.Equal(1, consumer.LastProgressReservationCut));
        Assert.Equal(0, Purge(db, 3).StoredRecords); db.Commit(new PutDocument("orders", "unblocked", "{}"));
    }
    [Fact]
    public void AConsumerCannotSpendTheReserveAgainUntilTheRetainedPrefixAdvances()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 2, ReservedOutboxRecords = 2 });
        db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}")); var consumer = Configure(db);
        Complete(db, consumer, db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1)), new PutDocument("derived", "a", "{}")).Get<ProjectionBatchResult>();
        var second = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        Assert.Equal(ErrorCode.ResourceExhausted, Complete(db, consumer, second, new PutDocument("derived", "b", "{}")).Error);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.Equal(1, status.Consumers.Single().Checkpoint); Assert.Equal(3, status.Head.Tail);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "derived", "b")));
        Assert.Equal(2, Purge(db, 1).FirstAvailable);
        Complete(db, consumer, second, new PutDocument("derived", "b", "{}")).Get<ProjectionBatchResult>();
        status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.Equal(2, status.Consumers.Single().Checkpoint); Assert.Equal(2, status.Consumers.Single().LastProgressReservationCut);
    }
    [Fact]
    public void TheHardRecordCeilingRollsBackEffectsCheckpointAndReservationTogether()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 1 });
        db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}")); var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        Assert.Equal(ErrorCode.ResourceExhausted, Complete(db, consumer, batch,
            new PutDocument("derived", "a", "{}"), new PutDocument("derived", "b", "{}")).Error);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.Equal(1, status.Head.Tail); Assert.Equal(0, status.Consumers.Single().Checkpoint);
        Assert.Equal(-1, status.Consumers.Single().LastProgressReservationCut);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "derived", "a")));
        Complete(db, consumer, batch, new PutDocument("derived", "a", "{}")).Get<ProjectionBatchResult>();
    }
    [Fact]
    public void ReservedBytesAreAlsoBoundedAndAFailedEffectDoesNotConsumeThem()
    {
        using var db = new TestDatabase(new() { MaxOutboxBytes = 1_500, ReservedOutboxBytes = 1_500 });
        db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}")); var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var large = new PutDocument("derived", "large", JsonSerializer.Serialize(new { data = new string('x', 2_000) }));
        Assert.Equal(ErrorCode.ResourceExhausted, Complete(db, consumer, batch, large).Error);
        Assert.Equal(-1, db.Database.GetOutboxStatus("root", db.Partition).Consumers.Single().LastProgressReservationCut);
        Complete(db, consumer, batch, new PutDocument("derived", "small", "{}")).Get<ProjectionBatchResult>();
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        Assert.InRange(status.Head.StoredBytes, 1_501, 3_000); Assert.Equal(1, status.Consumers.Single().LastProgressReservationCut);
    }
    [Fact]
    public void TheProgressFenceAndOriginalReceiptSurviveCheckpointCompactionAndReopen()
    {
        var limits = new DatabaseLimits { MaxOutboxRecords = 2, ReservedOutboxRecords = 2 };
        using var db = new TestDatabase(limits); db.Configure("orders", ResourceKind.Collection); db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}")); var consumer = Configure(db);
        var first = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        var original = Complete(db, consumer, first, new PutDocument("derived", "a", "{}", 0)).Get<ProjectionBatchResult>();
        db.Store.Compact(); db.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(db.Directory)); var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), limits);
        Assert.Equal(1, engine.GetOutboxStatus("root", db.Partition).Consumers.Single().LastProgressReservationCut);
        OperationResult Submit(ProjectionBatch batch, Mutation[] effects)
        {
            var id = Guid.NewGuid(); return engine.Apply(new(id, OperationKind.CommitProjectionBatch, "root", DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(new CommitProjectionBatchRequest(id, consumer, batch.Token, effects), JsonDefaults.Options)));
        }
        var replay = Submit(first, [new PutDocument("derived", "a", "{}", 0)]).Get<ProjectionBatchResult>();
        Assert.True(replay.AlreadyProcessed); Assert.Equal(original.Receipt.Token, replay.Receipt.Token);
        Assert.Equal(ErrorCode.ResourceExhausted, Submit(engine.ReadProjectionBatch("root", new(consumer, Limit: 1)),
            [new PutDocument("derived", "b", "{}")]).Error);
    }
}
