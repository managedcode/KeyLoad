using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ProjectionProgressTests
{
    private static ProjectionConsumerRef Configure(TestDatabase db, string name = "projection-v1")
    {
        var consumer = new ProjectionConsumerRef(db.Partition, name);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer,
            new(1, ["orders"], ["putDocument"])), id: id).Get<ProjectionConsumerInfo>();
        return consumer;
    }
    private static OperationResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), id: id);
    }
    private static OperationResult Write(TestDatabase db, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [.. effects]), id: id);
    }
    private static OutboxHead Purge(TestDatabase db, long through)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, through), id: id).Get<OutboxHead>();
    }
    [Test]
    public async Task AFullOutboxAllowsProjectionProgressButStillStopsOrdinaryProducers()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 1 });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        var completed = Complete(db, consumer, batch, new PutDocument("derived", "effect", "{}", 0)).Get<ProjectionBatchResult>();
        await Assert.That(completed.Checkpoint).IsEqualTo(1);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(2);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(1);
        await Assert.That(Write(db, new PutDocument("orders", "blocked", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "blocked"))).IsNull();
        await Assert.That(Complete(db, consumer, batch, new PutDocument("derived", "effect", "{}", 0)).Get<ProjectionBatchResult>().AlreadyProcessed).IsTrue();
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(2);
        // A filtered batch can advance without spending reserve, allowing the derived entries to be reclaimed too.
        var filtered = db.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(filtered.Entries).IsEmpty();
        Complete(db, consumer, filtered).Get<ProjectionBatchResult>();
        await Assert.That(Purge(db, 2).StoredRecords).IsEqualTo(0);
        Write(db, new PutDocument("orders", "next", "{}")).Get<CommitReceipt>();
    }
    [Test]
    public async Task EachPinnedConsumerCanAdvanceIndependentlyAndCleanupUnblocksThePartition()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 2 });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        var first = Configure(db, "first");
        var second = Configure(db, "second");
        Complete(db, first, db.Database.ReadProjectionBatch("root", new(first)), new PutDocument("derived", "first", "{}")).Get<ProjectionBatchResult>();
        Complete(db, second, db.Database.ReadProjectionBatch("root", new(second, Limit: 1)), new PutDocument("derived", "second", "{}")).Get<ProjectionBatchResult>();
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(3);
        foreach (var consumer in status.Consumers)
        {
            await Assert.That(consumer.Checkpoint).IsEqualTo(1);
        }

        await Assert.That(Purge(db, 1).StoredRecords).IsEqualTo(2);
        foreach (var consumer in new[] { first, second })
        {
            Complete(db, consumer, db.Database.ReadProjectionBatch("root", new(consumer))).Get<ProjectionBatchResult>();
        }

        foreach (var consumer in db.Database.GetOutboxStatus("root", db.Partition).Consumers)
        {
            await Assert.That(consumer.LastProgressReservationCut).IsEqualTo(1);
        }

        await Assert.That(Purge(db, 3).StoredRecords).IsEqualTo(0);
        db.Commit(new PutDocument("orders", "unblocked", "{}"));
    }
    [Test]
    public async Task AConsumerCannotSpendTheReserveAgainUntilTheRetainedPrefixAdvances()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 2, ReservedOutboxRecords = 2 });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var consumer = Configure(db);
        Complete(db, consumer, db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1)), new PutDocument("derived", "a", "{}")).Get<ProjectionBatchResult>();
        var second = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        await Assert.That(Complete(db, consumer, second, new PutDocument("derived", "b", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(1);
        await Assert.That(status.Head.Tail).IsEqualTo(3);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "derived", "b"))).IsNull();
        await Assert.That(Purge(db, 1).FirstAvailable).IsEqualTo(2);
        Complete(db, consumer, second, new PutDocument("derived", "b", "{}")).Get<ProjectionBatchResult>();
        status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(2);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(2);
    }
    [Test]
    public async Task TheHardRecordCeilingRollsBackEffectsCheckpointAndReservationTogether()
    {
        using var db = new TestDatabase(new() { MaxOutboxRecords = 1, ReservedOutboxRecords = 1 });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(Complete(db, consumer, batch,
            new PutDocument("derived", "a", "{}"), new PutDocument("derived", "b", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Head.Tail).IsEqualTo(1);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(0);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(-1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "derived", "a"))).IsNull();
        Complete(db, consumer, batch, new PutDocument("derived", "a", "{}")).Get<ProjectionBatchResult>();
    }
    [Test]
    public async Task ReservedBytesAreAlsoBoundedAndAFailedEffectDoesNotConsumeThem()
    {
        var small = new PutDocument("derived", "small", "{}");
        var large = new PutDocument("derived", "large", JsonSerializer.Serialize(new { data = new string('x', 2_000) }));
        var bytes = ProjectionNativeByteFixture.Calibrate(small, large);
        await Assert.That(bytes.Large).IsGreaterThan(bytes.Small);
        using var db = new TestDatabase(new() { MaxOutboxBytes = bytes.Input, ReservedOutboxBytes = bytes.Small });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.StoredBytes).IsEqualTo(bytes.Input);
        var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(Write(db, new PutDocument("orders", "blocked", "{}")).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "blocked"))).IsNull();
        await Assert.That(Complete(db, consumer, batch, large).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var failed = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(failed.Head.StoredBytes).IsEqualTo(bytes.Input);
        await Assert.That(failed.Head.Tail).IsEqualTo(1);
        await Assert.That(failed.Consumers.Single().Checkpoint).IsEqualTo(0);
        await Assert.That(failed.Consumers.Single().LastProgressReservationCut).IsEqualTo(-1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "derived", "large"))).IsNull();
        Complete(db, consumer, batch, small).Get<ProjectionBatchResult>();
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Head.StoredBytes).IsEqualTo(checked(bytes.Input + bytes.Small));
        await Assert.That(status.Head.StoredBytes).IsGreaterThan(bytes.Input).And.IsLessThanOrEqualTo(checked(bytes.Input + bytes.Small));
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(1);
    }
    [Test]
    public async Task OneByteShortProjectionReserveRollsBackEffectsCheckpointAndReservation()
    {
        var small = new PutDocument("derived", "small", "{}");
        var large = new PutDocument("derived", "large", JsonSerializer.Serialize(new { data = new string('x', 2_000) }));
        var bytes = ProjectionNativeByteFixture.Calibrate(small, large);
        using var db = new TestDatabase(new() { MaxOutboxBytes = bytes.Input, ReservedOutboxBytes = bytes.Small - 1 });
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "input", "{}"));
        var consumer = Configure(db);
        var batch = db.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(Complete(db, consumer, batch, small).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var status = db.Database.GetOutboxStatus("root", db.Partition);
        await Assert.That(status.Head.StoredBytes).IsEqualTo(bytes.Input);
        await Assert.That(status.Head.Tail).IsEqualTo(1);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(0);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(-1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "derived", "small"))).IsNull();
    }
    [Test]
    public async Task TheProgressFenceAndOriginalReceiptSurviveCheckpointCompactionAndReopen()
    {
        var limits = new DatabaseLimits { MaxOutboxRecords = 2, ReservedOutboxRecords = 2 };
        using var db = new TestDatabase(limits);
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var consumer = Configure(db);
        var first = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        var original = Complete(db, consumer, first, new PutDocument("derived", "a", "{}", 0)).Get<ProjectionBatchResult>();
        db.Store.Compact();
        db.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(db.Directory));
        var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        await Assert.That(engine.GetOutboxStatus("root", db.Partition).Consumers.Single().LastProgressReservationCut).IsEqualTo(1);
        OperationResult Submit(ProjectionBatch batch, Mutation[] effects)
        {
            var id = Guid.NewGuid();
            return engine.Apply(new(id, OperationKind.CommitProjectionBatch, "root", TimeProvider.System.GetUtcNow(),
                JsonSerializer.Serialize(new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), JsonDefaults.Options)));
        }
        var replay = Submit(first, [new PutDocument("derived", "a", "{}", 0)]).Get<ProjectionBatchResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(original.Receipt.Token);
        await Assert.That(Submit(engine.ReadProjectionBatch("root", new(consumer, Limit: 1)),
            [new PutDocument("derived", "b", "{}")]).Error).IsEqualTo(ErrorCode.ResourceExhausted);
    }
}
