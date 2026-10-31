using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>The vector cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class VectorBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(VectorScenario.Load, VectorScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task ExactTop10() => Run(VectorScenario.ExactTop10, VectorScenario.ExactTop10Async);

    private Task Run(string caseId, Func<IVectorDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Vector, caseId, VectorDrivers.Create, run);
}
