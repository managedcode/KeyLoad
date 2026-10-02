namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphReadBudgetTests
{
    private const string Orders = "orders";
    private const string Links = "links";
    private const string RootIdentity = "root";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string VertexA = "a";
    private const string VertexB = "b";
    private const string VertexC = "c";
    private const string VertexD = "d";
    private const string EdgeAB = "ab";
    private const string EdgeBC = "bc";
    private const string EdgeBD = "bd";
    private const string NextLabel = "next";
    private const string WalkLabel = "walk";
    private const string SkipLabel = "skip";
    private const int PaddingLength = 12_000;
    private const int ReadByteLimit = 4_096;
    private const int MaximumEdges = 2;

    [Test]
    public async Task GraphEdgeAndVertexDereferencesConsumeTheReadByteBudget()
    {
        using var database = new TestDatabase(new() { MaxQueryReadBytes = ReadByteLimit });
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Links, ResourceKind.Graph);
        EntityRef Vertex(string id) => new(database.Partition, Orders, id);
        database.Commit(
            new PutDocument(Orders, VertexA, "{}"),
            new PutDocument(Orders, VertexB, "{\"padding\":\"" + new string('x', PaddingLength) + "\"}"),
            new UpsertEdge(Links, EdgeAB, Vertex(VertexA), Vertex(VertexB), NextLabel));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Traverse(RootIdentity, database.Partition,
            Links, Vertex(VertexA), cancellationToken: TestContext.Current!.Execution.CancellationToken)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FilteredOrHiddenGraphEdgesCountAgainstTheGlobalVisitBudget(bool hidden)
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Links, ResourceKind.Graph);
        EntityRef Vertex(string id) => new(database.Partition, Orders, id);
        database.Commit(
            new PutDocument(Orders, VertexA, "{}", Access: new(Alice)),
            new PutDocument(Orders, VertexB, "{}", Access: new(Alice)),
            new PutDocument(Orders, VertexC, "{}", Access: new(Bob)),
            new PutDocument(Orders, VertexD, "{}", Access: new(Bob)),
            new UpsertEdge(Links, EdgeAB, Vertex(VertexA), Vertex(VertexB), WalkLabel),
            new UpsertEdge(Links, EdgeBC, Vertex(VertexB), Vertex(VertexC), SkipLabel),
            new UpsertEdge(Links, EdgeBD, Vertex(VertexB), Vertex(VertexD), SkipLabel));
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(Alice, Tenant,
            [new(DatabaseScope, Orders, Capability.DocumentsRead), new(DatabaseScope, Links, Capability.GraphRead)], [])
        { OwnerId = Alice, RestrictRows = true })).Get<PrincipalRecord>();

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Traverse(hidden ? Alice : RootIdentity,
            database.Partition, Links, Vertex(VertexA), maxEdges: MaximumEdges,
            labels: hidden ? null : [WalkLabel], cancellationToken: TestContext.Current!.Execution.CancellationToken)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
