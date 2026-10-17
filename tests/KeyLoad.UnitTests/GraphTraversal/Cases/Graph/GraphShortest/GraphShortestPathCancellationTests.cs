using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathCancellationTests
{
    private const string Nodes = "cancellation-nodes";
    private const string ProbeId = "base-source";
    private const string ScanId = "busy-source";
    private const string MissingTargetId = "no-target";
    private const int CandidateCount = 4_096;
    private const int CommitBatchSize = 128;

    [Test]
    public async Task AlreadyCanceledShortestPathDoesNoReadAndHealthyFollowupSucceeds()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, "already-canceled");
        var target = GraphShortestPathTestSupport.Ref(Nodes, MissingTargetId);
        GraphShortestPathTestSupport.PersistVertices(database, [source]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: cancellation.Token);
        var request = GraphShortestPathTestSupport.Request(database, source, target);
        var position = database.Store.Position;

        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request, budget));
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Found).IsFalse();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task InProgressAdjacencyScanCancelsOnChargedBytesAndSettlesBeforeHealthyRead()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var probe = GraphShortestPathTestSupport.Ref(Nodes, ProbeId);
        var source = GraphShortestPathTestSupport.Ref(Nodes, ScanId);
        var target = GraphShortestPathTestSupport.Ref(Nodes, MissingTargetId);
        GraphShortestPathTestSupport.PersistVertices(database, [probe, source, target]);
        PersistCandidates(database, source, CandidateCount);
        var request = GraphShortestPathTestSupport.Request(database, source, target,
            maxDepth: 1, maxVertices: 1_000, maxEdges: CandidateCount,
            labels: [GraphShortestPathTestSupport.Walk]);
        var probeRequest = GraphShortestPathTestSupport.Request(database, probe, target,
            maxDepth: 1, maxVertices: 1_000, maxEdges: CandidateCount,
            labels: [GraphShortestPathTestSupport.Walk]);
        var probeBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        _ = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, probeRequest, probeBudget);
        var sourcePosition = database.Store.Position;
        var image = GraphShortestPathFullStoreImage.Bytes(database.Store);
        var before = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        var clock = new GraphShortestPathObservedWorkClock();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            clock, cancellation.Token);
        clock.Arm(() => budget.ReadBytes > probeBudget.ReadBytes
            && database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
            cancellation.Cancel);
        GraphShortestPathResult? partial = null;
        OperationCanceledException canceled;
        try
        {
            canceled = Assert.ThrowsExactly<OperationCanceledException>(() => partial = database.Database.ShortestPath(
                GraphShortestPathTestSupport.RootPrincipal, request, budget));
        }
        finally
        {
            clock.Disarm();
        }
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(partial).IsNull();
        await Assert.That(budget.ReadBytes).IsGreaterThan(probeBudget.ReadBytes);
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(GraphShortestPathFullStoreImage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        var expected = new GraphShortestPathResult(1, false, null, [], [], sourcePosition);
        await Assert.That(JsonDefaults.Serialize(healthy).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(GraphShortestPathFullStoreImage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static void PersistCandidates(TestDatabase database,
        GraphShortestPathReferenceVertex source, int count)
    {
        for (var offset = 0; offset < count; offset += CommitBatchSize)
        {
            var end = Math.Min(count, offset + CommitBatchSize);
            var edges = new GraphShortestPathReferenceEdge[end - offset];
            for (var index = offset; index < end; index++)
            {
                edges[index - offset] = new($"candidate-{index:D5}", source,
                    GraphShortestPathTestSupport.Ref(Nodes, MissingTargetId), GraphShortestPathTestSupport.Skip);
            }
            GraphShortestPathTestSupport.PersistEdges(database, edges);
        }
    }
}
