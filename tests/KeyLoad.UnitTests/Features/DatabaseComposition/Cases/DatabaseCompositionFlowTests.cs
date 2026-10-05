using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionFlowTests
{
    private const string Collection = "people";
    private const string Graph = "knowledge";
    private const string Queue = "actions";
    private const string First = "alice";
    private const string Second = "bob";
    private const string Message = "link-1";
    private const string EdgePrefix = "edge-";
    private const string MessagePrefix = "message-";
    private const string Label = "knows";
    private const string Root = "root";
    private const string ReadySpace = "ready";
    private const string MetadataSpace = "message-meta";
    private const string EdgeSpace = "edge";
    private const int ThreeEffects = 3;

    [Test]
    public async Task AcComp002And003ForwardAndReverseKeepSourcesAndCanonicalReferences()
    {
        using var db = NewDatabase();
        var from = Vertex(db, First);
        var to = Vertex(db, Second);
        var payload = JsonSerializer.Serialize(new QueueGraphLink(from, to, Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var sourceBefore = db.Database.InspectMessage(Root, new(db.Partition, Queue), Message);

        var projected = db.Commit(new QueueToGraph(Graph, Queue, EdgePrefix));
        var graph = db.Database.Traverse(Root, db.Partition, Graph, from);
        var source = db.Database.InspectMessage(Root, new(db.Partition, Queue), Message);

        await Assert.That(projected.Mutations).HasSingleItem();
        await Assert.That(graph.Edges).HasSingleItem();
        await Assert.That(graph.Edges[0].Id).IsEqualTo(EdgePrefix + Message);
        await Assert.That(graph.Edges[0].From).IsEqualTo(from);
        await Assert.That(graph.Edges[0].To).IsEqualTo(to);
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(source.PayloadJson).IsEqualTo(sourceBefore!.PayloadJson);

        var reverse = db.Commit(new GraphToQueueMutation(Queue, Graph, from, MessagePrefix));
        var derived = db.Database.InspectMessage(Root, new(db.Partition, Queue), MessagePrefix + EdgePrefix + Message);
        var link = JsonSerializer.Deserialize<QueueGraphLink>(derived!.PayloadJson!, JsonDefaults.Options);
        await Assert.That(reverse.Mutations).HasSingleItem();
        await Assert.That(derived.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(link!.From).IsEqualTo(from);
        await Assert.That(link.To).IsEqualTo(to);
        await Assert.That(link.Label).IsEqualTo(Label);
    }

    [Test]
    public async Task AcComp004OrderedBatchSeesPriorStagedWritesAndLateConflictRollsBack()
    {
        using var db = NewDatabase(withVertices: false);
        var from = Vertex(db, First);
        var to = Vertex(db, Second);
        var payload = JsonSerializer.Serialize(new QueueGraphLink(from, to, Label), JsonDefaults.Options);
        var receipt = db.Commit(new PutDocument(Collection, First, "{}"), new PutDocument(Collection, Second, "{}"),
            new EnqueueMessage(Queue, Message, payload), new QueueToGraph(Graph, Queue, EdgePrefix));
        await Assert.That(receipt.Mutations.Length).IsEqualTo(4);
        await Assert.That(db.Database.Traverse(Root, db.Partition, Graph, from).Edges).HasSingleItem();

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(
            new QueueToGraph(Graph, Queue, "rollback-"), new QueueToGraph(Graph, Queue, "rollback-")));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RevisionConflict);
        var missing = db.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition(EdgeSpace, db.Partition, Graph, "rollback-" + Message)));
        await Assert.That(missing).IsNull();
        await Assert.That(db.Database.Traverse(Root, db.Partition, Graph, from).Edges).HasSingleItem();
    }

    [Test]
    public async Task AcComp002EmptyScheduledAndExpiredReadySourcesProduceNoEffects()
    {
        using var db = NewDatabase();
        var empty = db.Commit(new QueueToGraph(Graph, Queue, EdgePrefix));
        await Assert.That(empty.Mutations).IsEmpty();

        var at = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, First), Vertex(db, Second), Label), JsonDefaults.Options);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, [
            new EnqueueMessage(Queue, "scheduled", payload, NotBefore: at.AddMinutes(1)),
            new EnqueueMessage(Queue, "expired", payload, ExpiresAt: at.AddSeconds(1))]);
        db.Submit(OperationKind.Batch, command, id: command.CommandId, time: at).Get<CommitReceipt>();
        var projection = new CommandRequest(Guid.NewGuid(), db.Partition, [new QueueToGraph(Graph, Queue, EdgePrefix)]);
        var result = db.Submit(OperationKind.Batch, projection, id: projection.CommandId, time: at.AddSeconds(2)).Get<CommitReceipt>();

        await Assert.That(result.Mutations).IsEmpty();
        var state = db.Store.Read(view => view.GetRecord<MessageMetadata>(
            KeySpace.Partition(MetadataSpace, db.Partition, Queue, "expired")));
        await Assert.That(state!.State).IsEqualTo(MessageState.Ready);
    }

    [Test]
    public async Task AcComp006ReadyLookaheadRejectsIncompleteSourceWithoutWriting()
    {
        using var db = NewDatabase();
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, First), Vertex(db, Second), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, "a", payload), new EnqueueMessage(Queue, "b", payload));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new QueueToGraph(Graph, Queue, EdgePrefix, MaxMessages: 1)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Database.Traverse(Root, db.Partition, Graph, Vertex(db, First)).Edges).IsEmpty();
        var ready = db.Store.Read(view => view.Scan(KeySpace.Partition(ReadySpace, db.Partition, Queue), 2));
        await Assert.That(ready.Records.Length).IsEqualTo(2);
    }

    [Test]
    public async Task AcComp004CommittedEffectsAndRejectedEffectsSurviveRealStoreReopen()
    {
        var db = NewDatabase();
        try
        {
            var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, First), Vertex(db, Second), Label), JsonDefaults.Options);
            db.Commit(new EnqueueMessage(Queue, Message, payload));
            db.Commit(new QueueToGraph(Graph, Queue, EdgePrefix));
            Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new QueueToGraph(Graph, Queue, "rollback-"),
                new QueueToGraph(Graph, Queue, "rollback-")));

            db.Store.Dispose();
            using var reopened = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var committed = reopened.Read(view => view.GetRecord<EdgeRecord>(
                KeySpace.Partition(EdgeSpace, db.Partition, Graph, EdgePrefix + Message)));
            var rejected = reopened.Read(view => view.GetRecord<EdgeRecord>(
                KeySpace.Partition(EdgeSpace, db.Partition, Graph, "rollback-" + Message)));
            await Assert.That(committed).IsNotNull();
            await Assert.That(rejected).IsNull();
        }
        finally
        {
            db.Dispose();
        }
    }

    [Test]
    public async Task AcComp006ExactExpandedEffectCapSucceedsAndCumulativeOverflowRollsBack()
    {
        using var db = NewDatabase(limits: new() { MaxBatchMutations = ThreeEffects });
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, First), Vertex(db, Second), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, "a", payload), new EnqueueMessage(Queue, "b", payload),
            new EnqueueMessage(Queue, "c", payload));

        var exact = db.Commit(new QueueToGraph(Graph, Queue, "exact-", MaxMessages: ThreeEffects));
        await Assert.That(exact.Mutations.Length).IsEqualTo(ThreeEffects);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(
            new QueueToGraph(Graph, Queue, "overflow-a-", MaxMessages: ThreeEffects),
            new QueueToGraph(Graph, Queue, "overflow-b-", MaxMessages: ThreeEffects)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var absent = db.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition(EdgeSpace, db.Partition, Graph, "overflow-a-a")));
        await Assert.That(absent).IsNull();
    }

    private static TestDatabase NewDatabase(bool withVertices = true, DatabaseLimits? limits = null)
    {
        var db = new TestDatabase(limits);
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue);
        if (withVertices)
        {
            db.Commit(new PutDocument(Collection, First, "{}"), new PutDocument(Collection, Second, "{}"));
        }
        return db;
    }

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);
}
