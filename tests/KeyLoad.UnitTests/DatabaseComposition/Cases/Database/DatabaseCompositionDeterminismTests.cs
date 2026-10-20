using System.Text.Json;
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
    private const long NextSequence = 1;
    private const int EvaluationDelayMinutes = 5;

    [Test]
    public async Task AcComp006IdenticalReplicatedCommandIgnoresDivergentLocalClocks()
    {
        await DatabaseCompositionReplicaCut.RunAsync(Seed, async (left, right) =>
        {
            var expectedFrom = DatabaseCompositionExpectedReferences.Capture(left, Vertex(left, From));
            var expectedTo = DatabaseCompositionExpectedReferences.Capture(left, Vertex(left, To));
            var fixedEvaluation = TimeProvider.System.GetUtcNow().AddMinutes(EvaluationDelayMinutes);
            var commandId = Guid.NewGuid();
            var request = new CommandRequest(commandId, left.Partition,
                [new QueueToGraph(Graph, Queue, EdgePrefix),
                    new GraphToQueueMutation(Queue, Graph, Vertex(left, From), MessagePrefix)]);
            var work = left.CreateReadWork(TestContext.Current!.Execution.CancellationToken);
            var operation = left.Database.CreateCommand(OperationKind.Batch, commandId, "root", fixedEvaluation,
                OrleansSerialization.Serialize(request), work);
            var limits = new DatabaseLimits { QueryDeadlineSeconds = OneSecondDeadline };
            var steady = new DivergentClock(DateTimeOffset.UnixEpoch, timestampStep: 0);
            var jumping = new DivergentClock(DateTimeOffset.UnixEpoch.AddYears(50), TimeSpan.TicksPerHour);
            var leftEngine = new DatabaseEngine(left.Store, left.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), steady);
            var rightEngine = new DatabaseEngine(right.Store, right.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), jumping);

            var probe = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), jumping);
            var elapsedFailure = Assert.ThrowsExactly<KeyLoadException>(probe.Check);
            await Assert.That(elapsedFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            var sequence = checked(leftEngine.LastApplied + NextSequence);
            await Assert.That(rightEngine.LastApplied).IsEqualTo(sequence - NextSequence);
            var callerToken = TestContext.Current!.Execution.CancellationToken;
            var leftReceipt = leftEngine.ApplyStorageOperation(operation, sequence, callerToken).Result.Get<CommitReceipt>();
            var rightReceipt = rightEngine.ApplyStorageOperation(operation, sequence, callerToken).Result.Get<CommitReceipt>();

            await Assert.That(leftReceipt.Mutations.Length).IsEqualTo(2);
            foreach (var effect in leftReceipt.Mutations)
            {
                await Assert.That(effect.CompositionReferences.SequenceEqual([expectedFrom, expectedTo])).IsTrue();
            }
            await Assert.That(OrleansSerialization.Serialize(rightReceipt).SequenceEqual(OrleansSerialization.Serialize(leftReceipt))).IsTrue();
            await Assert.That(rightReceipt.Mutations.Select(Effect).SequenceEqual(leftReceipt.Mutations.Select(Effect))).IsTrue();
            await Assert.That(SameStored<EdgeRecord>(left, right, EdgeSpace, Graph, EdgePrefix + SourceId)).IsTrue();
            await Assert.That(SameStored<MessageMetadata>(left, right, MessageMetadataSpace, Queue,
                MessagePrefix + EdgePrefix + SourceId)).IsTrue();
            await Assert.That(SameStored<MessageBody>(left, right, MessageBodySpace, Queue,
                MessagePrefix + EdgePrefix + SourceId)).IsTrue();
            await Assert.That(leftEngine.LastApplied).IsEqualTo(sequence);
            await Assert.That(rightEngine.LastApplied).IsEqualTo(sequence);
        });
    }

    private static (string Kind, string Resource, string Id, long Revision) Effect(MutationReceipt receipt)
        => (receipt.Kind, receipt.Resource, receipt.Id, receipt.Revision);

    private static bool SameStored<T>(TestDatabase left, DatabaseCompositionReplicaCut right, string space, string resource, string id)
        where T : class
    {
        T Read(IAtomicStore store) => store.Read(view => view.GetRecord<T>(
            KeySpace.Partition(space, left.Partition, resource, id)))!;
        return OrleansSerialization.Serialize(Read(left.Store)).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(Read(right.Store)));
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
