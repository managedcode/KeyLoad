namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphTraversalForeignFrontierFixture
{
    private const string SourcePartition = "scope-source";
    private const string ForeignPartition = "scope-target";
    private const string SourceId = "source";
    private const string CollidingId = "collision";
    private const string LocalTailId = "tail";
    private const string ContinuedId = "z-continuation";
    private const string SourceEdgeId = "source-link";
    private const string LocalEdgeId = "local-only";
    private const string ContinuedEdgeId = "z-continuation-link";
    private const string Label = "walk";
    private const string Attributes = "{}";
    private const long InitialRevision = 1;

    internal static GraphTraversalForeignFrontierState Seed(TestDatabase database)
    {
        var source = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(SourcePartition), SourceId));
        var foreign = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(ForeignPartition), CollidingId));
        var collision = GraphCrossPartitionTestSupport.Vertex(source.Partition, CollidingId);
        var tail = GraphCrossPartitionTestSupport.Vertex(source.Partition, LocalTailId);
        var edge = new EdgeRecord(SourceEdgeId, source, foreign, Label, Attributes, InitialRevision);
        CommandRequest[] commands =
        [
            GraphStorageReferenceFixture.Command(source.Partition,
                new PutDocument(source.Collection, source.Id, Attributes), new PutDocument(collision.Collection, collision.Id, Attributes),
                new PutDocument(tail.Collection, tail.Id, Attributes)),
            GraphStorageReferenceFixture.Command(foreign.Partition, new PutDocument(foreign.Collection, foreign.Id, Attributes)),
            GraphStorageReferenceFixture.Command(source.Partition, Mutation(edge),
                new UpsertEdge(GraphCrossPartitionTestSupport.Graph, LocalEdgeId, collision, tail, Label, Attributes))
        ];
        var history = commands.Select(command => (command,
            GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>())).ToArray();
        return new(source, foreign, edge, history);
    }

    internal static GraphTraversalIntegrityContinuation Continue(DatabaseEngine owner, GraphTraversalForeignFrontierState state)
    {
        var target = state.Source with { Id = ContinuedId };
        var edge = new EdgeRecord(ContinuedEdgeId, state.Source, target, Label, Attributes, InitialRevision);
        var command = GraphStorageReferenceFixture.Command(state.Source.Partition,
            new PutDocument(target.Collection, target.Id, Attributes), Mutation(edge));
        return new(command, GraphStorageReferenceFixture.Submit(owner, command).Get<CommitReceipt>(), edge);
    }

    private static UpsertEdge Mutation(EdgeRecord edge)
        => new(GraphCrossPartitionTestSupport.Graph, edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson);
}
