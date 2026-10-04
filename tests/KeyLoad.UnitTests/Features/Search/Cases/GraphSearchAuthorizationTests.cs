using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchAuthorizationTests
{
    private const string Alice = "graph-alice";
    private const string Bob = "graph-bob";
    private const string SecretGrant = "pii.use";

    [Test]
    public async Task HiddenIntermediateIsNeverExpandedAndAnExplicitHiddenSeedFails()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        AddRestrictedGraph(database);
        GraphSearchTestSupport.PersistReader(database, restrictRows: true, ownerId: Alice);
        var engine = new SearchEngine(database.Database);
        var hiddenSeedWalk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle));
        var hiddenSeed = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            GraphSearchTestSupport.Reader, new(1, RetrievalRequest(database),
                Retriever: new(hiddenSeedWalk)), TestContext.Current!.Execution.CancellationToken)))!;
        var visibleWalk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root));

        var result = await engine.GraphSearchAsync(GraphSearchTestSupport.Reader,
            new(1, RetrievalRequest(database), Retriever: new(visibleWalk)),
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(hiddenSeed.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(result.Hits).IsEmpty();
    }

    [Test]
    public async Task RequestedLabelRequiresPersistedFieldUseAndAuthorizedGrantAllowsIt()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database, protectLabels: true);
        GraphSearchTestSupport.AddPath(database);
        GraphSearchTestSupport.PersistReader(database);
        var walk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root))
        with
        { Labels = [GraphSearchTestSupport.Label] };
        var search = RetrievalRequest(database);
        var engine = new SearchEngine(database.Database);

        var denied = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            GraphSearchTestSupport.Reader, new(1, search, Retriever: new(walk)),
            TestContext.Current!.Execution.CancellationToken)))!;
        var zeroWeightDenied = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            GraphSearchTestSupport.Reader, new(1, search, Retriever: new(walk, Weight: 0)),
            TestContext.Current!.Execution.CancellationToken)))!;
        GraphSearchTestSupport.PersistReader(database, fieldGrants: [SecretGrant]);
        var allowed = await engine.GraphSearchAsync(GraphSearchTestSupport.Reader,
            new(1, search, Retriever: new(walk)), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(zeroWeightDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(allowed.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static SearchRequest RetrievalRequest(TestDatabase database)
        => new(database.Partition, GraphSearchTestSupport.Documents, Limit: 10);

    private static void AddRestrictedGraph(TestDatabase database)
    {
        database.Commit(
            new PutDocument(GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root, "{}", Access: new(Alice)),
            new PutDocument(GraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle, "{}", Access: new(Bob)),
            new PutDocument(GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit, "{}", Access: new(Alice)));
        var root = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var hidden = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle);
        var endpoint = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit);
        database.Commit(new UpsertEdge(GraphSearchTestSupport.Graph, "restricted-root-hidden", root, hidden,
                GraphSearchTestSupport.Label),
            new UpsertEdge(GraphSearchTestSupport.Graph, "restricted-hidden-endpoint", hidden, endpoint,
                GraphSearchTestSupport.Label));
    }
}
