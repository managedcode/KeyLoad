using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;

using static KeyLoad.UnitTests.Features.Messaging.SubscriptionTestActions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionCurrentEventIdentityTrial
{
    internal static async Task RunAsync()
    {
        using var db = new TestDatabase();
        db.Configure("streams", ResourceKind.StreamSet);
        db.Configure("topic", ResourceKind.Topic);
        var originalReceipt = db.Commit(new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        await Assert.That(originalReceipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(originalReceipt.Mutations[0].Kind).IsEqualTo("appendEvents");
        db.Store.Dispose();

        using var store = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var stream = new EventSourceRef(db.Partition, "streams", EventSourceKind.Stream, "a");

        var duplicateId = Guid.NewGuid();
        var duplicate = database.Apply(new(duplicateId, OperationKind.Batch, "root", database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new CommandRequest(duplicateId, db.Partition,
                [new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.Any)]), JsonDefaults.Options)));
        await Assert.That(duplicate.Error).IsEqualTo(ErrorCode.DuplicateEventId);
        var changedId = Guid.NewGuid();
        var changed = database.Apply(new(changedId, OperationKind.Batch, "root", database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new CommandRequest(changedId, db.Partition,
                [new AppendEvents("streams", "a", [new("same", "Created", "{\"different\":true}")], ExpectedStreamRevision.Any)]), JsonDefaults.Options)));
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);

        var retained = database.ReadEventSource("root", new ReadEventSourceRequest(stream));
        await Assert.That(retained.Head.TailPosition).IsEqualTo(1);
        await Assert.That(retained.Events.Length).IsEqualTo(1);
        await Assert.That(retained.Events[0].Position).IsEqualTo(1);
        await Assert.That(retained.Events[0].EventSequence).IsEqualTo(1);
        await Assert.That(retained.Events[0].Data.PayloadJson).IsEqualTo("{}");

        var otherStreamId = Guid.NewGuid();
        var otherStreamReceipt = database.Apply(new(otherStreamId, OperationKind.Batch, "root", database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new CommandRequest(otherStreamId, db.Partition,
                [new AppendEvents("streams", "b", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream)]), JsonDefaults.Options)));
        await Assert.That(otherStreamReceipt.Get<CommitReceipt>().Mutations.Length).IsEqualTo(1);
        var topicId = Guid.NewGuid();
        var topicReceipt = database.Apply(new(topicId, OperationKind.Batch, "root", database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new CommandRequest(topicId, db.Partition,
                [new PublishTopic("topic", [new("same", "Created", "{}")])]), JsonDefaults.Options)));
        await Assert.That(topicReceipt.Get<CommitReceipt>().Mutations.Length).IsEqualTo(1);
        var topicConflictId = Guid.NewGuid();
        var topicConflict = database.Apply(new(topicConflictId, OperationKind.Batch, "root", database.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(new CommandRequest(topicConflictId, db.Partition,
                [new PublishTopic("topic", [new("same", "Other", "{}")])]), JsonDefaults.Options)));
        await Assert.That(topicConflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.ReadEventSource("root", new ReadEventSourceRequest(Topic(db))).Head.TailPosition).IsEqualTo(1);
        await Assert.That(database.ReadEventSource("root", new ReadEventSourceRequest(
            new EventSourceRef(db.Partition, "streams", EventSourceKind.Stream, "b"))).Events.Length).IsEqualTo(1);
        await Assert.That(database.ReadEventSource("root", new ReadEventSourceRequest(Topic(db))).Events[0].Data.EventType).IsEqualTo("Created");
    }
}
