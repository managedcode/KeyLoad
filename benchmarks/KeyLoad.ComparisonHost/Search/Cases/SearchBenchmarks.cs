using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>The full-text cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class SearchBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(SearchScenario.Load, SearchScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task Index() => Run(SearchScenario.Index, SearchScenario.IndexAsync);

    [Test, DependsOn(nameof(Index), ProceedOnFailure = true)]
    public Task TermQuery() => Run(SearchScenario.TermQuery, SearchScenario.TermQueryAsync);

    [Test, DependsOn(nameof(TermQuery), ProceedOnFailure = true)]
    public Task AndQuery() => Run(SearchScenario.AndQuery, SearchScenario.AndQueryAsync);

    private Task Run(string caseId, Func<ISearchDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Search, caseId, SearchDrivers.Create, run);
}
