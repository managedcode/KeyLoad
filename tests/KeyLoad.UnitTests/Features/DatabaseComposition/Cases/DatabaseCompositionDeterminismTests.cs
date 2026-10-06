using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionDeterminismTests
{
    private const string Collection = "entities";
    private const string Graph = "relations";
    private const string Queue = "actions";
    private const string From = "first";
    private const string To = "second";
    private const string SourceId = "source";
    private const string EdgePrefix = "edge-";
    private const string MessagePrefix = "message-";
    private const string Label = "related";
    private const string EdgeSpace = "edge";
    private const string MessageBodySpace = "message-body";
    private const string MessageMetadataSpace = "message-meta";
    private const int OneSecondDeadline = 1;
    private const long ReplicationIndex = 42;
    private const int EvaluationDelayMinutes = 5;

    [Test]
    public async Task AcComp006IdenticalReplicatedCommandIgnoresDivergentLocalClocks()
    {
        using var left = Seed();
        using var right = Seed();
        var fixedEvaluation = TimeProvider.System.GetUtcNow().AddMinutes(EvaluationDelayMinutes);
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, left.Partition,
            [new QueueToGraph(Graph, Queue, EdgePrefix),
                new GraphToQueueMutation(Queue, Graph, Vertex(left, From), MessagePrefix)]);
        var operation = new ReplicatedOperation(commandId, OperationKind.Batch, "root", fixedEvaluation,
            JsonSerializer.Serialize(request, JsonDefaults.Options));
        var limits = new DatabaseLimits { QueryDeadlineSeconds = OneSecondDeadline };
        var steady = new DivergentClock(DateTimeOffset.UnixEpoch, timestampStep: 0);
        var jumping = new DivergentClock(DateTimeOffset.UnixEpoch.AddYears(50), TimeSpan.TicksPerHour);
        var leftEngine = new DatabaseEngine(left.Store, left.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution(), steady);
        var rightEngine = new DatabaseEngine(right.Store, right.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution(), jumping);

        var probe = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), jumping);
        var elapsedFailure = Assert.ThrowsExactly<KeyLoadException>(probe.Check);
        await Assert.That(elapsedFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var leftReceipt = leftEngine.Apply(operation, ReplicationIndex).Get<CommitReceipt>();
        var rightReceipt = rightEngine.Apply(operation, ReplicationIndex).Get<CommitReceipt>();

        await Assert.That(leftReceipt.Mutations.Length).IsEqualTo(2);
        await Assert.That(rightReceipt.Mutations.Select(Effect).SequenceEqual(leftReceipt.Mutations.Select(Effect))).IsTrue();
        await Assert.That(SameStored<EdgeRecord>(left, right, EdgeSpace, Graph, EdgePrefix + SourceId)).IsTrue();
        await Assert.That(SameStored<MessageMetadata>(left, right, MessageMetadataSpace, Queue,
            MessagePrefix + EdgePrefix + SourceId)).IsTrue();
        await Assert.That(SameStored<MessageBody>(left, right, MessageBodySpace, Queue,
            MessagePrefix + EdgePrefix + SourceId)).IsTrue();
        await Assert.That(leftEngine.LastApplied).IsEqualTo(ReplicationIndex);
        await Assert.That(rightEngine.LastApplied).IsEqualTo(ReplicationIndex);
    }

    private static (string Kind, string Resource, string Id, long Revision) Effect(MutationReceipt receipt)
        => (receipt.Kind, receipt.Resource, receipt.Id, receipt.Revision);

    private static bool SameStored<T>(TestDatabase left, TestDatabase right, string space, string resource, string id)
        where T : class
    {
        T Read(TestDatabase db) => db.Store.Read(view => view.GetRecord<T>(
            KeySpace.Partition(space, db.Partition, resource, id)))!;
        return NativeSerialization.Serialize(Read(left)).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(Read(right)));
    }

    private static TestDatabase Seed()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue);
        db.Commit(new PutDocument(Collection, From, "{}"), new PutDocument(Collection, To, "{}"));
        var link = new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label);
        db.Commit(new EnqueueMessage(Queue, SourceId, JsonSerializer.Serialize(link, JsonDefaults.Options)));
        return db;
    }

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    private sealed class DivergentClock(DateTimeOffset wallTime, long timestampStep) : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => wallTime;

        public override long GetTimestamp() => Interlocked.Add(ref timestamp, timestampStep);
    }
}
