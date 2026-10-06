using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenSearchNativeVectorRegression
{
    private const string FirstNode = "isolated-opensearch-1", Http = "http", GuidFormat = "N";
    private const int QueryNumber = 170, HigherNeighbor = 2289, LowerNeighbor = 1272;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>AC-BC-FAIL-010: real post-start native1/2/3 resources retain exact double rank, projection and caller cancellation.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        var endpoint = app.GetEndpoint(FirstNode, Http);
        await VerifyPrecisionAsync(endpoint, nodeCount, token);
        await OpenSearchNativeVectorEdges.VerifyAsync(endpoint, nodeCount, token);
    }

    private static async Task VerifyPrecisionAsync(Uri endpoint, int nodeCount, CancellationToken token)
    {
        var dataset = OpenSearchVectorQueryTests.WitnessDataset();
        using var client = new HttpClient { BaseAddress = endpoint, Timeout = RequestTimeout };
        await using var target = new OpenSearchTarget(client, Guid.NewGuid().ToString(GuidFormat), OpenSearchNames.ExpectedImage,
            Topology(nodeCount), NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.Read());
        await target.InitializeAsync(dataset, token);
        await using var session = await target.OpenSessionAsync(token);
        var query = dataset.Documents[QueryNumber];
        var result = await session.ExecuteAsync(Scenario.VectorExact, query, token);
        await ComparisonValidation.ValidateOperationAsync(session, dataset, Scenario.VectorExact, query, result, token);
        await Assert.That(result.Neighbors.HasValue).IsTrue();
        var neighbors = result.Neighbors!.Value;
        await Assert.That(neighbors[4].Id).IsEqualTo(dataset.Documents[HigherNeighbor].Id);
        await Assert.That(neighbors[5].Id).IsEqualTo(dataset.Documents[LowerNeighbor].Id);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancellation.CancelAsync();
        var canceled = session.ExecuteAsync(Scenario.VectorExact, query, cancellation.Token);
        await Assert.That(async () => await canceled).Throws<OperationCanceledException>();
        await Assert.That(canceled.IsCanceled).IsTrue();
        token.ThrowIfCancellationRequested();
    }

    internal static ComparisonTopology Topology(int nodeCount) => nodeCount switch
    {
        1 => ComparisonTopology.Standalone,
        2 => ComparisonTopology.TwoNode,
        3 => ComparisonTopology.Replicated,
        _ => throw new ArgumentOutOfRangeException(nameof(nodeCount))
    };
}
