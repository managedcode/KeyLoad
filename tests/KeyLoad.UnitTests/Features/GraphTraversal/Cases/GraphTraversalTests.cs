namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphTraversalTests
{
    private const string Orders = "orders";
    private const string Links = "links";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string RootIdentity = "root";
    private const string VertexA = "a";
    private const string VertexB = "b";
    private const string VertexC = "c";
    private const string EdgeAB = "ab";
    private const string EdgeBC = "bc";
    private const string EdgeCA = "ca";
    private const string NextLabel = "next";
    private const int MaximumDepth = 10;

    [Test]
    public async Task TraversalStopsAtHiddenIntermediateVerticesAndHandlesCycles()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Links, ResourceKind.Graph);
        database.Commit(
            new PutDocument(Orders, VertexA, "{}", Access: new(Alice)),
            new PutDocument(Orders, VertexB, "{}", Access: new(Bob)),
            new PutDocument(Orders, VertexC, "{}", Access: new(Alice)));
        EntityRef Vertex(string id) => new(database.Partition, Orders, id);
        database.Commit(
            new UpsertEdge(Links, EdgeAB, Vertex(VertexA), Vertex(VertexB), NextLabel),
            new UpsertEdge(Links, EdgeBC, Vertex(VertexB), Vertex(VertexC), NextLabel),
            new UpsertEdge(Links, EdgeCA, Vertex(VertexC), Vertex(VertexA), NextLabel));
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(Alice, Tenant,
            [new(DatabaseScope, Orders, Capability.DocumentsRead), new(DatabaseScope, Links, Capability.GraphRead)], [])
        { OwnerId = Alice, RestrictRows = true })).Get<PrincipalRecord>();

        var hidden = database.Database.Traverse(Alice, database.Partition, Links, Vertex(VertexA),
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(hidden.Vertices).HasSingleItem();
        await Assert.That(hidden.Edges).IsEmpty();
        await Assert.That(database.Database.Traverse(RootIdentity, database.Partition, Links, Vertex(VertexA), maxDepth: MaximumDepth,
            cancellationToken: TestContext.Current!.Execution.CancellationToken).Vertices.Length).IsEqualTo(3);
    }
}
