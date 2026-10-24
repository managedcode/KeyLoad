using System.Text.Json;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class ProjectionProgressTests
{
    [Test]
    public async Task TheOriginalReceiptSurvivesCheckpointCompactionAndReopenAndTheNextBatchCommits()
    {
        var limits = new DatabaseLimits();
        using var db = new TestDatabase(limits);
        ProjectionProgressOperations.ConfigureResources(db);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var consumer = ProjectionProgressOperations.Configure(db);
        var first = db.Database.ReadProjectionBatch("root", new(consumer, Limit: 1));
        var original = ProjectionProgressOperations.Complete(db, consumer, first, new PutDocument("derived", "a", "{}", 0)).Get<ProjectionBatchResult>();
        db.Store.Compact();
        db.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var engine = new DatabaseEngine(reopened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        await Assert.That(engine.GetOutboxStatus("root", db.Partition).Consumers.Single().Checkpoint).IsEqualTo(1);
        Result<object> Submit(ProjectionBatch batch, Mutation[] effects)
        {
            var id = Guid.NewGuid();
            return engine.Apply(engine.CreateCommand(OperationKind.CommitProjectionBatch, id, "root", TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), JsonDefaults.Options)));
        }
        var replay = Submit(first, [new PutDocument("derived", "a", "{}", 0)]).Get<ProjectionBatchResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(original.Receipt.Token);
        var next = Submit(engine.ReadProjectionBatch("root", new(consumer, Limit: 1)),
            [new PutDocument("derived", "b", "{}")]).Get<ProjectionBatchResult>();
        await Assert.That(next.AlreadyProcessed).IsFalse();
        await Assert.That(next.Checkpoint).IsEqualTo(2);
        await Assert.That(engine.GetDocument("root", new(db.Partition, "derived", "b"))).IsNotNull();
    }
}
