namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphTraversalProjectionTests
{
    private const string Collection = "vertices";
    private const string Graph = "links";
    private const string Root = "root";
    private const string Reader = "reader";
    private const string Other = "other";
    private const string Label = "next";

    [Test]
    [Arguments(GraphTraversalProjection.Full)]
    [Arguments(GraphTraversalProjection.Vertices)]
    [Arguments(GraphTraversalProjection.Count)]
    public async Task AcGraph001ProjectionsKeepDepthCyclesDistinctCountingAndFreshRowVisibility(GraphTraversalProjection projection)
    {
        using var db = Create();
        var request = Request(db, projection);
        var before = db.Store.Position;
        var result = db.Database.Traverse(Root, request);

        await Assert.That(result.VertexCount).IsEqualTo(3);
        await Assert.That(result.Vertices.Length).IsEqualTo(projection == GraphTraversalProjection.Count ? 0 : 3);
        await Assert.That(result.Edges.Length).IsEqualTo(projection == GraphTraversalProjection.Full ? 4 : 0);
        await Assert.That(db.Database.Traverse(Root, request with { MaxDepth = 0 }).VertexCount).IsEqualTo(1);
        await Assert.That(db.Database.Traverse(Root, request with { MaxDepth = 1 }).VertexCount).IsEqualTo(2);
        await Assert.That(db.Database.Traverse(Reader, request).VertexCount).IsEqualTo(1);
        await Assert.That(db.Store.Position).IsEqualTo(before);
        _ = db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(new(Reader, "tenant", [], []) { PolicyEpoch = 2 })).Get<PrincipalRecord>();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Traverse(Reader, request));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.Traverse(Root, request).VertexCount).IsEqualTo(3);
        await Assert.That(JsonDefaults.Deserialize<global::KeyLoad.GraphTraversal>(JsonDefaults.Serialize(result)).VertexCount).IsEqualTo(3);
        await Assert.That(OrleansSerialization.Deserialize<global::KeyLoad.GraphTraversal>(OrleansSerialization.Serialize(result)).VertexCount).IsEqualTo(3);
    }

    [Test]
    [Arguments(GraphTraversalProjection.Full)]
    [Arguments(GraphTraversalProjection.Vertices)]
    [Arguments(GraphTraversalProjection.Count)]
    public async Task AcGraph001EveryProjectionRetainsWorkBoundsCancellationAndHealthyFollowUp(GraphTraversalProjection projection)
    {
        using var db = Create();
        var request = Request(db, projection);
        var edgeLimit = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Traverse(Root, request with { MaxEdges = 1 }));
        var vertexLimit = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Traverse(Root, request with { MaxVertices = 1 }));
        await Assert.That(edgeLimit.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(vertexLimit.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _ = Assert.ThrowsExactly<OperationCanceledException>(() => db.Database.Traverse(Root, request, cancellation.Token));
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Traverse(Root,
            request with { Projection = (GraphTraversalProjection)int.MaxValue }));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.Traverse(Root, request).VertexCount).IsEqualTo(3);
    }

    private static TraverseRequest Request(TestDatabase db, GraphTraversalProjection projection)
        => new(db.Partition, Graph, new(db.Partition, Collection, "a"), MaxDepth: 5, MaxVertices: 10, MaxEdges: 10)
        { Projection = projection };

    private static TestDatabase Create()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Commit(new PutDocument(Collection, "a", "{}", Access: new(Reader)),
            new PutDocument(Collection, "b", "{}", Access: new(Other)), new PutDocument(Collection, "c", "{}", Access: new(Reader)));
        EntityRef Vertex(string id) => new(db.Partition, Collection, id);
        db.Commit(new UpsertEdge(Graph, "ab", Vertex("a"), Vertex("b"), Label),
            new UpsertEdge(Graph, "ab-again", Vertex("a"), Vertex("b"), Label),
            new UpsertEdge(Graph, "bc", Vertex("b"), Vertex("c"), Label),
            new UpsertEdge(Graph, "ca", Vertex("c"), Vertex("a"), Label));
        _ = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, "tenant",
            [new("database", Collection, Capability.DocumentsRead), new("database", Graph, Capability.GraphRead)], [])
        { OwnerId = Reader, RestrictRows = true })).Get<PrincipalRecord>();
        return db;
    }
}
