using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionBudgetTests
{
    private const string Collection = "entities";
    private const string Graph = "links";
    private const string Queue = "jobs";
    private const string From = "first";
    private const string To = "second";
    private const string Message = "message";
    private const string Prefix = "derived-";
    private const string Label = "related";
    private const string EdgeSpace = "edge";
    private const long TinyByteBudget = 1;

    [Test]
    public async Task AcComp006OneSharedRawByteBudgetRejectsBeforeEffects()
    {
        using var db = NewDatabase();
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var limited = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = TinyByteBudget
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var request = new CommandRequest(Guid.NewGuid(), db.Partition, [new QueueToGraph(Graph, Queue, Prefix)]);
        var operation = limited.CreateCommand(OperationKind.Batch, request.CommandId, "root", TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => limited.Apply(operation).Get<CommitReceipt>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(StoredEdge(db)).IsNull();
    }

    [Test]
    public async Task AcComp006ExactCumulativeReadBytesPassAndOneByteLessRollsBack()
    {
        using var db = NewDatabase();
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var single = SingleProjectionReadBytes(db);
        var exact = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = 2 * single
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var shortBudget = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = 2 * single - 1
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());

        CommitReceipt Send(DatabaseEngine engine, string prefix)
        {
            var request = new CommandRequest(Guid.NewGuid(), db.Partition,
                [new QueueToGraph(Graph, Queue, prefix + "a-"), new QueueToGraph(Graph, Queue, prefix + "b-")]);
            // Prepare the genuine original through the fixture before measuring deterministic apply's source ledger.
            var operation = db.Database.CreateCommand(OperationKind.Batch, request.CommandId, "root",
                TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(request));
            return engine.Apply(operation).Get<CommitReceipt>();
        }
        var accepted = Send(exact, "exact-");
        await Assert.That(accepted.Mutations.Length).IsEqualTo(2);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Send(shortBudget, "short-"));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var absent = db.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition(EdgeSpace, db.Partition, Graph, "short-a-" + Message)));
        await Assert.That(absent).IsNull();
    }

    private static long SingleProjectionReadBytes(TestDatabase db)
        => DatabaseCompositionReadCost.SingleUntyped(db, Queue, Message, Vertex(db, From), Vertex(db, To));

    private static TestDatabase NewDatabase()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue);
        db.Commit(new PutDocument(Collection, From, "{}"), new PutDocument(Collection, To, "{}"));
        return db;
    }

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    private static EdgeRecord? StoredEdge(TestDatabase db) => db.Store.Read(view => view.GetRecord<EdgeRecord>(
        KeySpace.Partition(EdgeSpace, db.Partition, Graph, Prefix + Message)));
}
