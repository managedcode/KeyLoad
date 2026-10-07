namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphStorageAtomicEffectsTests
{
    private const string NewVertex = "new-vertex";
    private const string Edge = "atomic-edge";
    private const string RejectedVertex = "rejected-vertex";
    private const string Stream = "atomic";
    private const string Message = "atomic-message";
    private const string RejectedStream = "rejected";
    private const string RejectedMessage = "rejected-message";

    [Test]
    public async Task Kl022EntityEdgeEventQueueAtomicCommitRetryAndLateEdgeConflictLeaveExactModelState()
    {
        using var database = new TestDatabase();
        GraphStorageReferenceFixture.Configure(database);
        var target = GraphStorageReferenceFixture.Vertex(database.Partition, GraphStorageReferenceFixture.VertexId(0));
        var command = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphStorageReferenceFixture.Nodes, NewVertex, "{}", 0),
            new UpsertEdge(GraphStorageReferenceFixture.Graph, Edge,
                GraphStorageReferenceFixture.Vertex(database.Partition, NewVertex), target, GraphStorageReferenceFixture.Label, "{}", 0),
            new AppendEvents(GraphStorageReferenceFixture.Events, Stream, [new("created", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(GraphStorageReferenceFixture.Queue, Message, "{}"));
        var committed = GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>();
        var reference = new Dictionary<string, GraphStorageExpectedEdge>(StringComparer.Ordinal)
        { [Edge] = new(Edge, NewVertex, target.Id, GraphStorageReferenceFixture.Label, "{}", 1) };
        await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference, [NewVertex]);
        var position = database.Store.Position;
        var retried = GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>();
        await Assert.That(retried.Token).IsEqualTo(committed.Token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var existing = GraphStorageRejectedOutcomeAssertions.CaptureState(database, NewVertex, Stream, Message);
        var rejected = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphStorageReferenceFixture.Nodes, RejectedVertex, "{}", 0),
            new AppendEvents(GraphStorageReferenceFixture.Events, RejectedStream, [new("rejected", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(GraphStorageReferenceFixture.Queue, RejectedMessage, "{}"),
            new UpsertEdge(GraphStorageReferenceFixture.Graph, Edge,
                GraphStorageReferenceFixture.Vertex(database.Partition, NewVertex), target, GraphStorageReferenceFixture.Label, "{}", 0));
        await GraphStorageRejectedOutcomeAssertions.VerifyReplayAsync(database, rejected, ErrorCode.RevisionConflict,
            "The expected revision does not match.", position);
        await GraphStorageRejectedOutcomeAssertions.VerifyStateAsync(database, NewVertex, Stream, Message, existing);
        await Assert.That(database.Database.GetDocument(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Nodes, RejectedVertex))).IsNull();
        await Assert.That(database.Database.ReadStream(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Events, RejectedStream)).Events).IsEmpty();
        await Assert.That(database.Database.InspectMessage(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Queue), RejectedMessage)).IsNull();
        var events = database.Database.ReadStream(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Events, Stream)).Events;
        await Assert.That(events).HasSingleItem();
        await Assert.That(events[0].Data.EventId).IsEqualTo("created");
        await Assert.That(events[0].Data.EventType).IsEqualTo("Created");
        await Assert.That(events[0].Data.PayloadJson).IsEqualTo("{}");
        await Assert.That(events[0].Revision).IsEqualTo(1);
        await Assert.That(database.Database.GetDocument(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Nodes, NewVertex))!.Revision).IsEqualTo(1);
        await Assert.That(database.Database.InspectMessage(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Queue), Message)!.PayloadJson).IsEqualTo("{}");
        await Assert.That(database.Database.InspectMessage(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Queue), Message)!.Metadata.State).IsEqualTo(MessageState.Ready);
        await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference, [NewVertex]);
        GraphStorageReferenceFixture.Submit(database.Database, GraphStorageReferenceFixture.Command(database.Partition,
            new DeleteEdge(GraphStorageReferenceFixture.Graph, Edge, 1))).Get<CommitReceipt>();
        reference.Clear();
        await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference, [NewVertex]);
    }

    [Test]
    public async Task Kl022MissingEndpointRejectsTheWholeEntityEdgeBatchAndFollowingMutationSucceeds()
    {
        using var database = new TestDatabase();
        GraphStorageReferenceFixture.Configure(database);
        var source = GraphStorageReferenceFixture.Vertex(database.Partition, GraphStorageReferenceFixture.VertexId(0));
        var position = database.Store.Position;
        var existing = GraphStorageRejectedOutcomeAssertions.CaptureState(database, source.Id, RejectedStream, RejectedMessage);
        var rejected = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphStorageReferenceFixture.Nodes, RejectedVertex, "{}", 0),
            new UpsertEdge(GraphStorageReferenceFixture.Graph, Edge, source,
                GraphStorageReferenceFixture.Vertex(database.Partition, "missing"), GraphStorageReferenceFixture.Label));
        await GraphStorageRejectedOutcomeAssertions.VerifyReplayAsync(database, rejected, ErrorCode.NotFound,
            "The graph vertex is unavailable.", position);
        await GraphStorageRejectedOutcomeAssertions.VerifyStateAsync(database, source.Id, RejectedStream, RejectedMessage, existing);
        await Assert.That(database.Database.GetDocument(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Nodes, RejectedVertex))).IsNull();
        var reference = new Dictionary<string, GraphStorageExpectedEdge>(StringComparer.Ordinal);
        await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference);
        var target = GraphStorageReferenceFixture.Vertex(database.Partition, GraphStorageReferenceFixture.VertexId(1));
        GraphStorageReferenceFixture.Submit(database.Database, GraphStorageReferenceFixture.Command(database.Partition,
            new UpsertEdge(GraphStorageReferenceFixture.Graph, Edge, source, target, GraphStorageReferenceFixture.Label, "{}", 0)))
            .Get<CommitReceipt>();
        reference[Edge] = new(Edge, source.Id, target.Id, GraphStorageReferenceFixture.Label, "{}", 1);
        await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference);
    }
}
