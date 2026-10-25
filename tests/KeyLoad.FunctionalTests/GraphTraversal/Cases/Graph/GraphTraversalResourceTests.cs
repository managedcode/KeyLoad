namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphTraversalResourceTests
{
    private const string Collection = "orders";
    private const string Graph = "links";
    private const string Root = "root";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Walk = "walk";
    private const string Skip = "skip";
    private const int LargePayloadCharacters = 12_000;

    [Test]
    public async Task AcMp005_ConvergingLargeTargetUsesOneVisibilityLookup()
    {
        using var db = new TestDatabase();
        Configure(db);
        var start = Vertex(db, "a");
        var left = Vertex(db, "b");
        var right = Vertex(db, "c");
        var target = Vertex(db, "d");
        db.Commit(new PutDocument(Collection, start.Id, "{}"), new PutDocument(Collection, left.Id, "{}"),
            new PutDocument(Collection, right.Id, "{}"), new PutDocument(Collection, target.Id, "{}"),
            new UpsertEdge(Graph, "ab", start, left, Walk), new UpsertEdge(Graph, "ac", start, right, Walk),
            new UpsertEdge(Graph, "bd", left, target, Walk), new UpsertEdge(Graph, "cd", right, target, Walk));

        var before = db.Store.GetReadDiagnostics();
        _ = db.Database.Traverse(Root, db.Partition, Graph, start,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        var smallBytes = db.Store.GetReadDiagnostics().PointExaminedBytes - before.PointExaminedBytes;
        var largeJson = LargeDocument();
        db.Commit(new PutDocument(Collection, target.Id, largeJson, ExpectedRevision: 1, ExplicitReplacement: true));
        before = db.Store.GetReadDiagnostics();
        var traversal = db.Database.Traverse(Root, db.Partition, Graph, start,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        var largeBytes = db.Store.GetReadDiagnostics().PointExaminedBytes - before.PointExaminedBytes;
        var payloadDelta = System.Text.Encoding.UTF8.GetByteCount(largeJson) - 2;
        await Assert.That(Math.Abs((largeBytes - smallBytes) - payloadDelta)).IsLessThanOrEqualTo(64);
        await Assert.That(traversal.Vertices.Select(vertex => vertex.Id))
            .IsEquivalentTo(new[] { "a", "b", "c", "d" });
        await Assert.That(traversal.Edges.Select(edge => edge.Id))
            .IsEquivalentTo(new[] { "ab", "ac", "bd", "cd" });
    }

    [Test]
    public async Task AcMp006_RepeatedHiddenTargetIsNotExpanded()
    {
        using var db = new TestDatabase(new()
        {
        });
        Configure(db);
        var start = Vertex(db, "a");
        var hidden = Vertex(db, "b");
        var downstream = Vertex(db, "c");
        db.Commit(new PutDocument(Collection, start.Id, "{}", Access: new(Alice)),
            new PutDocument(Collection, hidden.Id, LargeDocument(), Access: new(Bob)),
            new PutDocument(Collection, downstream.Id, "{}", Access: new(Alice)),
            new UpsertEdge(Graph, "ab1", start, hidden, Walk), new UpsertEdge(Graph, "ab2", start, hidden, Walk),
            new UpsertEdge(Graph, "bc", hidden, downstream, Walk));
        AuthorizeAlice(db);

        var traversal = db.Database.Traverse(Alice, db.Partition, Graph, start,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        await Assert.That(traversal.Vertices).HasSingleItem();
        await Assert.That(traversal.Edges).IsEmpty();
    }

    [Test]
    public async Task AcMp005_FilteredAndCyclicEdgesStillConsumeGlobalVisitLimit()
    {
        using var db = new TestDatabase();
        Configure(db);
        var start = Vertex(db, "a");
        var next = Vertex(db, "b");
        db.Commit(new PutDocument(Collection, start.Id, "{}"), new PutDocument(Collection, next.Id, "{}"),
            new UpsertEdge(Graph, "aa", start, start, Skip), new UpsertEdge(Graph, "ab", start, next, Walk),
            new UpsertEdge(Graph, "ba", next, start, Walk));
        var token = TestContext.Current!.Execution.CancellationToken;

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Traverse(Root, db.Partition, Graph, start,
            maxEdges: 2, labels: [Walk], cancellationToken: token)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var traversal = db.Database.Traverse(Root, db.Partition, Graph, start, maxEdges: 3, labels: [Walk], cancellationToken: token);
        await Assert.That(traversal.Vertices.Length).IsEqualTo(2);
        await Assert.That(traversal.Edges.Select(edge => edge.Id)).IsEquivalentTo(new[] { "ab", "ba" });
    }

    private static string LargeDocument() => "{\"padding\":\"" + new string('x', LargePayloadCharacters) + "\"}";
    private static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);
    private static void Configure(TestDatabase db)
    {
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
    }
    private static void AuthorizeAlice(TestDatabase db) => db.Submit(OperationKind.ConfigurePrincipal,
        new ConfigurePrincipalRequest(new PrincipalRecord(Alice, "tenant",
            [new("database", Collection, Capability.DocumentsRead), new("database", Graph, Capability.GraphRead)], [])
        {
            OwnerId = Alice,
            RestrictRows = true
        })).Get<PrincipalRecord>();
}
