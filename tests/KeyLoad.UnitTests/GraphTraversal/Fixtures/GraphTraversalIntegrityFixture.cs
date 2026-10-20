namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphTraversalIntegrityFixture
{
    internal const string Graph = "traversal-integrity";
    internal const string Nodes = "traversal-integrity-nodes";
    internal const string Root = "root";
    internal const string Label = "walk";
    internal const string OtherLabel = "other";
    internal const string Attributes = "{}";
    internal const int Depth = 1;
    internal const int MaximumVertices = 4;
    internal const int MaximumEdges = 3;
    internal const long InitialRevision = 1;
    private const string Source = "source";
    private const string HealthyTarget = "healthy-target";
    private const string DamagedTarget = "damaged-target";
    private const string ContinuedTarget = "continuation-target";
    private const string HealthyEdgeId = "a-healthy";
    private const string DamagedEdgeId = "z-damaged";
    private const string ContinuedEdgeId = "zz-continuation";

    internal static GraphTraversalIntegrityState Seed(TestDatabase database)
    {
        database.Configure(Nodes, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph);
        var source = database.ExpectedReference(new(database.Partition, Nodes, Source));
        var healthy = new EdgeRecord(HealthyEdgeId, source, database.ExpectedReference(new(database.Partition, Nodes, HealthyTarget)),
            Label, Attributes, InitialRevision);
        var damaged = new EdgeRecord(DamagedEdgeId, source, database.ExpectedReference(new(database.Partition, Nodes, DamagedTarget)),
            Label, Attributes, InitialRevision);
        var command = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(Nodes, Source, Attributes), new PutDocument(Nodes, HealthyTarget, Attributes),
            new PutDocument(Nodes, DamagedTarget, Attributes), Mutation(healthy), Mutation(damaged));
        return new(command, GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>(), healthy, damaged);
    }

    internal static GraphTraversalIntegrityContinuation Continue(DatabaseEngine owner, GraphTraversalIntegrityState state)
    {
        var edge = new EdgeRecord(ContinuedEdgeId, state.HealthyEdge.From,
            state.HealthyEdge.From with { Id = ContinuedTarget }, Label, Attributes, InitialRevision);
        var command = GraphStorageReferenceFixture.Command(edge.From.Partition,
            new PutDocument(Nodes, ContinuedTarget, Attributes), Mutation(edge));
        return new(command, GraphStorageReferenceFixture.Submit(owner, command).Get<CommitReceipt>(), edge);
    }

    internal static UpsertEdge Mutation(EdgeRecord edge) => new(Graph, edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson);
}
