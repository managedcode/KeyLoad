using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>The graph cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class GraphBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(GraphScenario.Load, GraphScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task Neighbors() => Run(GraphScenario.Neighbors, GraphScenario.NeighborsAsync);

    [Test, DependsOn(nameof(Neighbors), ProceedOnFailure = true)]
    public Task Traverse() => Run(GraphScenario.Traverse, GraphScenario.TraverseAsync);

    private Task Run(string caseId, Func<IGraphDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Graph, caseId, GraphDrivers.Create, run);
}
