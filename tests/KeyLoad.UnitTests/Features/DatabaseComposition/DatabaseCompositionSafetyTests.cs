using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionSafetyTests
{
    private const string Collection = "entities";
    private const string Graph = "links";
    private const string Queue = "jobs";
    private const string From = "first";
    private const string To = "second";
    private const string Message = "message";
    private const string Prefix = "derived-";
    private const string Label = "related";
    private const string Principal = "limited";
    private const string SecretPath = "/label";
    private const string RawReadGrant = "raw.read";
    private const string RawUseGrant = "raw.use";
    private const string WriteGrant = "target.write";
    private const string EdgeSpace = "edge";
    private const string MessageMetadataSpace = "message-meta";
    private const string DifferentPartition = "different-partition";

    [Test]
    public async Task AcComp002MalformedMissingAndForeignLinksRejectWithoutDerivedEdges()
    {
        using var db = NewDatabase();
        var malformed = "{\"from\":null,\"to\":null,\"label\":\"related\"}";
        db.Commit(new EnqueueMessage(Queue, Message, malformed));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new QueueToGraph(Graph, Queue, Prefix)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(StoredEdge(db)).IsNull();

        using var foreignDb = NewDatabase();
        var foreign = new PartitionRef(foreignDb.Partition.TenantId, foreignDb.Partition.DatabaseId,
            foreignDb.Partition.TransactionDomainId, DifferentPartition);
        var link = new QueueGraphLink(new(foreign, Collection, From), Vertex(foreignDb, To), Label);
        foreignDb.Commit(new EnqueueMessage(Queue, Message, JsonSerializer.Serialize(link, JsonDefaults.Options)));
        var scopeFailure = Assert.ThrowsExactly<KeyLoadException>(() => foreignDb.Commit(new QueueToGraph(Graph, Queue, Prefix)));
        await Assert.That(scopeFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(StoredEdge(foreignDb)).IsNull();
    }

    [Test]
    public async Task AcComp002TamperedCanonicalPartitionDigestIsRejected()
    {
        using var db = NewDatabase();
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        var tampered = payload.Replace(db.Partition.AtomicPartitionId, new string('0', db.Partition.AtomicPartitionId.Length),
            StringComparison.Ordinal);
        db.Commit(new EnqueueMessage(Queue, Message, tampered));

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new QueueToGraph(Graph, Queue, Prefix)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(StoredEdge(db)).IsNull();
    }

    [Test]
    public async Task AcComp005SourceRawGrantsAndTargetWriteAreRequired()
    {
        using var db = NewDatabase(protectedQueue: true);
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var grants = new[]
        {
            new ScopeGrant(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
            new ScopeGrant(db.Partition.DatabaseId, Queue, Capability.QueueInspect),
            new ScopeGrant(db.Partition.DatabaseId, Graph, Capability.GraphWrite)
        };
        ConfigurePrincipal(db, grants, []);
        KeyLoadException Attempt() => Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            var request = new CommandRequest(Guid.NewGuid(), db.Partition, [new QueueToGraph(Graph, Queue, Prefix)]);
            db.Submit(OperationKind.Batch, request, Principal, request.CommandId).Get<CommitReceipt>();
        });
        var missingRead = Attempt();
        await Assert.That(missingRead.Code).IsEqualTo(ErrorCode.PermissionDenied);

        ConfigurePrincipal(db, grants, [RawReadGrant]);
        var missingUse = Attempt();
        await Assert.That(missingUse.Code).IsEqualTo(ErrorCode.PermissionDenied);
        ConfigurePrincipal(db, grants, [RawReadGrant, RawUseGrant]);
        var missingWrite = Attempt();
        await Assert.That(missingWrite.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(StoredEdge(db)).IsNull();
    }

    [Test]
    public async Task AcComp003HiddenNeighborDoesNotReachQueue()
    {
        using var db = NewDatabase(withVertices: false);
        db.Commit(new PutDocument(Collection, From, "{}", Access: new(Principal)),
            new PutDocument(Collection, To, "{}", Access: new("other")));
        db.Commit(new UpsertEdge(Graph, "hidden", Vertex(db, From), Vertex(db, To), Label));
        ConfigurePrincipal(db,
            [new(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
                new(db.Partition.DatabaseId, Graph, Capability.GraphRead),
                new(db.Partition.DatabaseId, Queue, Capability.QueuePublish)], []);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new GraphToQueueMutation(Queue, Graph, Vertex(db, From), Prefix)]);
        var result = db.Submit(OperationKind.Batch, command, Principal, command.CommandId).Get<CommitReceipt>();

        await Assert.That(result.Mutations).IsEmpty();
        var metadata = db.Store.Read(view => view.GetRecord<MessageMetadata>(
            KeySpace.Partition(MessageMetadataSpace, db.Partition, Queue, Prefix + "hidden")));
        await Assert.That(metadata).IsNull();
    }

    [Test]
    public async Task AcComp005StableRetryRechecksCurrentPolicyAndNewIdCollides()
    {
        using var db = NewDatabase();
        var payload = JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, From), Vertex(db, To), Label), JsonDefaults.Options);
        db.Commit(new EnqueueMessage(Queue, Message, payload));
        var allowed = new[]
        {
            new ScopeGrant(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
            new ScopeGrant(db.Partition.DatabaseId, Queue, Capability.QueueInspect),
            new ScopeGrant(db.Partition.DatabaseId, Graph, Capability.GraphWrite)
        };
        ConfigurePrincipal(db, allowed, [], restrictRows: false);
        var request = new CommandRequest(Guid.NewGuid(), db.Partition, [new QueueToGraph(Graph, Queue, Prefix)]);
        CommitReceipt Send(CommandRequest command) => db.Submit(OperationKind.Batch, command, Principal,
            command.CommandId).Get<CommitReceipt>();

        var first = Send(request);
        var replay = Send(request);
        await Assert.That(replay.CommandId).IsEqualTo(first.CommandId);
        await Assert.That(replay.Token).IsEqualTo(first.Token);
        await Assert.That(replay.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision))
            .SequenceEqual(first.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision))))
            .IsTrue();
        var collision = Assert.ThrowsExactly<KeyLoadException>(() => Send(request with { CommandId = Guid.NewGuid() }));
        await Assert.That(collision.Code).IsEqualTo(ErrorCode.RevisionConflict);

        ConfigurePrincipal(db, allowed[..^1], [], restrictRows: false);
        var revoked = Assert.ThrowsExactly<KeyLoadException>(() => Send(request));
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(StoredEdge(db)).IsNotNull();
    }

    private static TestDatabase NewDatabase(bool protectedQueue = false, bool withVertices = true)
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph, fields: protectedQueue ? [new(SecretPath, "secret", WriteGrant: WriteGrant)] : null);
        db.Configure(Queue, ResourceKind.WorkQueue,
            fields: protectedQueue ? [new(SecretPath, "secret", RawReadGrant, RawUseGrant)] : null);
        if (withVertices)
        {
            db.Commit(new PutDocument(Collection, From, "{}"), new PutDocument(Collection, To, "{}"));
        }
        return db;
    }

    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    private static EdgeRecord? StoredEdge(TestDatabase db) => db.Store.Read(view => view.GetRecord<EdgeRecord>(
        KeySpace.Partition(EdgeSpace, db.Partition, Graph, Prefix + Message)));

    private static void ConfigurePrincipal(TestDatabase db, ScopeGrant[] grants, string[] fields, bool restrictRows = true)
    {
        var previous = db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal)));
        var record = new PrincipalRecord(Principal, db.Partition.TenantId, [.. grants], [.. fields])
        {
            RestrictRows = restrictRows,
            OwnerId = Principal,
            PolicyEpoch = (previous?.PolicyEpoch ?? 0) + 1
        };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record)).Get<PrincipalRecord>();
    }
}
