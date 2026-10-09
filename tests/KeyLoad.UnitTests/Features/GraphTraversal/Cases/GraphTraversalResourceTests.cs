using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

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
    private const int OneLargeVertexReadBudget = 20_000;

    [Test]
    public async Task AcMp005_ConvergingLargeTargetUsesOneVisibilityLookup()
    {
        using var db = new TestDatabase(new() { MaxQueryReadBytes = OneLargeVertexReadBudget });
        Configure(db);
        var start = Vertex(db, "a");
        var left = Vertex(db, "b");
        var right = Vertex(db, "c");
        var target = Vertex(db, "d");
        db.Commit(new PutDocument(Collection, start.Id, "{}"), new PutDocument(Collection, left.Id, "{}"),
            new PutDocument(Collection, right.Id, "{}"), new PutDocument(Collection, target.Id, LargeDocument()),
            new UpsertEdge(Graph, "ab", start, left, Walk), new UpsertEdge(Graph, "ac", start, right, Walk),
            new UpsertEdge(Graph, "bd", left, target, Walk), new UpsertEdge(Graph, "cd", right, target, Walk));

        var traversal = db.Database.Traverse(Root, db.Partition, Graph, start,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        await Assert.That(traversal.Vertices.Select(vertex => vertex.Id))
            .IsEquivalentTo(new[] { "a", "b", "c", "d" });
        await Assert.That(traversal.Edges.Select(edge => edge.Id))
            .IsEquivalentTo(new[] { "ab", "ac", "bd", "cd" });
    }

    [Test]
    public async Task AcMp006_RepeatedHiddenTargetIsNotExpanded()
    {
        using var db = new TestDatabase(new() { MaxQueryReadBytes = OneLargeVertexReadBudget });
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

    [Test]
    public async Task AcMp012_ExactResponseLimitAndCancellationPreserveCommitPosition()
    {
        using var db = new TestDatabase();
        Configure(db);
        var start = Vertex(db, "a");
        db.Commit(new PutDocument(Collection, start.Id, "{}"));
        var expected = db.Database.Traverse(Root, db.Partition, Graph, start, maxDepth: 0);
        var bytes = JsonDefaults.Serialize(expected).Length;
        var exact = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = bytes }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var shortLimit = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = bytes - 1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        await Assert.That(exact.Traverse(Root, db.Partition, Graph, start, maxDepth: 0).Vertices)
            .IsEquivalentTo(expected.Vertices);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => shortLimit.Traverse(Root, db.Partition, Graph, start,
            maxDepth: 0)).Code).IsEqualTo(ErrorCode.BudgetExceeded);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await cancellation.CancelAsync();
        var position = db.Store.Position;
        await Assert.That(() => db.Database.Traverse(Root, db.Partition, Graph, start, cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Commit(new PutDocument(Collection, "after", "{}"));
        await Assert.That(db.Database.Traverse(Root, db.Partition, Graph, start, maxDepth: 0).Vertices).HasSingleItem();
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
        { OwnerId = Alice, RestrictRows = true })).Get<PrincipalRecord>();
}
