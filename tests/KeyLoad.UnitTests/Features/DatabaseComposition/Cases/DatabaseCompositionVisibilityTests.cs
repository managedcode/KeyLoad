using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionVisibilityTests
{
    private const string Collection = "entities";
    private const string Graph = "links";
    private const string Queue = "jobs";
    private const string From = "first";
    private const string To = "second";
    private const string Absent = "missing";
    private const string Principal = "reader";
    private const string Other = "other";
    private const string Message = "message";
    private const string Prefix = "derived-";
    private const string Label = "related";
    private const string EdgeSpace = "edge";

    [Test]
    public async Task AcComp002MissingEndpointRejectsWithoutWriting()
    {
        using var db = NewDatabase();
        db.Commit(new PutDocument(Collection, From, "{}"));
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, Absent), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new QueueToGraph(Graph, Queue, Prefix)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(Edge(db)).IsNull();
    }

    [Test]
    public async Task AcComp002HiddenEndpointRejectsWithoutLeakingRelationship()
    {
        using var db = NewDatabase();
        db.Commit(new PutDocument(Collection, From, "{}", Access: new(Principal)),
            new PutDocument(Collection, To, "{}", Access: new(Other)));
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var caller = new PrincipalRecord(Principal, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
                new(db.Partition.DatabaseId, Queue, Capability.QueueInspect),
                new(db.Partition.DatabaseId, Graph, Capability.GraphWrite)], [])
        { RestrictRows = true, OwnerId = Principal };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(caller)).Get<PrincipalRecord>();
        var request = new CommandRequest(Guid.NewGuid(), db.Partition, [new QueueToGraph(Graph, Queue, Prefix)]);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Submit(OperationKind.Batch,
            request, Principal, request.CommandId).Get<CommitReceipt>());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(Edge(db)).IsNull();
    }

    private static TestDatabase NewDatabase()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue);
        return db;
    }

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    private static EdgeRecord? Edge(TestDatabase db) => db.Store.Read(view => view.GetRecord<EdgeRecord>(
        KeySpace.Partition(EdgeSpace, db.Partition, Graph, Prefix + Message)));
}
