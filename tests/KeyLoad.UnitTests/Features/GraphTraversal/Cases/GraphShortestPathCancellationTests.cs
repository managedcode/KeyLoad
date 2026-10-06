using KeyLoad.Core;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathCancellationTests
{
    private const string Nodes = "cancellation-nodes";
    private const string ProbeId = "base-source";
    private const string ScanId = "busy-source";
    private const string MissingTargetId = "no-target";
    private const string NoReadMessage = "A canceled path read accepted storage bytes before cancellation.";
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
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(false);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: cancellation.Token);
        var observer = new GraphShortestPathCancellationObserver(budget, cancellation, started, probeBudget.ReadBytes);
        Exception? primary = null;
        OperationCanceledException? canceled = null;
        List<Exception> settlement;
        try
        {
            observer.StartAndWait();
            CapturePath(database, request, cancellation, budget, ref canceled, ref primary);
        }
        finally
        {
            settlement = observer.JoinAfterPath();
        }

        GraphShortestPathCancellationFailures.ThrowIfAny(primary, settlement);
        await Assert.That(canceled).IsNotNull();
        await Assert.That(canceled!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(observer.CancellationRequested).IsTrue();
        await Assert.That(observer.ObservedReadBytes).IsGreaterThan(probeBudget.ReadBytes);
        await Assert.That(database.Store.Position).IsEqualTo(sourcePosition);
        var healthy = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, probeRequest,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Found).IsFalse();
        await Assert.That(database.Store.Position).IsEqualTo(sourcePosition);
    }

    private static void CapturePath(TestDatabase database, GraphShortestPathRequest request,
        CancellationTokenSource cancellation, ReadExecutionBudget budget,
        ref OperationCanceledException? canceled, ref Exception? primary)
    {
        try
        {
            _ = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request, budget);
            primary = new InvalidOperationException(NoReadMessage);
        }
        catch (OperationCanceledException failure) when (cancellation.IsCancellationRequested)
        {
            canceled = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            primary = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            primary = failure;
        }
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
