using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionReverseTests
{
    private const string Collection = "entities";
    private const string Graph = "links";
    private const string Queue = "jobs";
    private const string From = "first";
    private const string To = "second";
    private const string FirstEdge = "a";
    private const string SecondEdge = "b";
    private const string Prefix = "derived-";
    private const string Label = "related";
    private const string MessageMetadataSpace = "message-meta";
    private const long SingleStoredMessage = 1;

    [Test]
    public async Task AcComp003LateQueueCollisionRollsBackEarlierDerivedMessage()
    {
        using var db = NewDatabase();
        SeedEdges(db);
        db.Commit(new EnqueueMessage(Queue, Prefix + SecondEdge, "{}"));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(
            new GraphToQueueMutation(Queue, Graph, Vertex(db, From), Prefix)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Metadata(db, Prefix + FirstEdge)).IsNull();
        await Assert.That(Metadata(db, Prefix + SecondEdge)).IsNotNull();
    }

    [Test]
    public async Task AcComp003QueueQuotaFailureRollsBackAllDerivedMessages()
    {
        using var db = NewDatabase(new() { MaxStoredMessages = SingleStoredMessage });
        SeedEdges(db);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(
            new GraphToQueueMutation(Queue, Graph, Vertex(db, From), Prefix)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Metadata(db, Prefix + FirstEdge)).IsNull();
        await Assert.That(Metadata(db, Prefix + SecondEdge)).IsNull();
    }

    [Test]
    public async Task AcComp006EqualSourceCutsProduceEqualDerivedContent()
    {
        using var left = NewDatabase();
        using var right = NewDatabase();
        SeedEdges(left);
        SeedEdges(right);

        left.Commit(new GraphToQueueMutation(Queue, Graph, Vertex(left, From), Prefix));
        right.Commit(new GraphToQueueMutation(Queue, Graph, Vertex(right, From), Prefix));
        var leftPayload = left.Database.InspectMessage("root", new(left.Partition, Queue), Prefix + FirstEdge)!.PayloadJson;
        var rightPayload = right.Database.InspectMessage("root", new(right.Partition, Queue), Prefix + FirstEdge)!.PayloadJson;
        await Assert.That(leftPayload).IsEqualTo(rightPayload);
        var link = JsonSerializer.Deserialize<QueueGraphLink>(leftPayload!, JsonDefaults.Options);
        await Assert.That(link!.From).IsEqualTo(Vertex(left, From));
        await Assert.That(link.To).IsEqualTo(Vertex(left, To));
    }

    private static TestDatabase NewDatabase(QueuePolicy? policy = null)
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue, queuePolicy: policy);
        db.Commit(new PutDocument(Collection, From, "{}"), new PutDocument(Collection, To, "{}"));
        return db;
    }

    private static void SeedEdges(TestDatabase db) => db.Commit(
        new UpsertEdge(Graph, FirstEdge, Vertex(db, From), Vertex(db, To), Label),
        new UpsertEdge(Graph, SecondEdge, Vertex(db, From), Vertex(db, To), Label));

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    private static MessageMetadata? Metadata(TestDatabase db, string id) => db.Store.Read(view =>
        view.GetRecord<MessageMetadata>(KeySpace.Partition(MessageMetadataSpace, db.Partition, Queue, id)));
}
