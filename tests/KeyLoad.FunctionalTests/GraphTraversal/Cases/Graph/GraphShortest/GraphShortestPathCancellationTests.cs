namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphShortestPathCancellationTests
{
    private const string Nodes = "cancellation-nodes";
    private const string MissingTargetId = "no-target";

    [Test]
    public async Task AlreadyCanceledShortestPathDoesNoReadAndHealthyFollowupSucceeds()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, "already-canceled");
        var target = GraphShortestPathTestSupport.Ref(Nodes, MissingTargetId);
        GraphShortestPathTestSupport.PersistVertices(database, [source]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(cancellationToken: cancellation.Token);
        var request = GraphShortestPathTestSupport.Request(database, source, target);
        var position = database.Store.Position;
        var reads = database.Store.GetReadDiagnostics();

        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request, budget));
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(reads);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Found).IsFalse();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
