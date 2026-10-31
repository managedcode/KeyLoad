using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>The analytics cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class AnalyticsBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(AnalyticsScenario.Load, AnalyticsScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task GroupBy() => Run(AnalyticsScenario.GroupBy, AnalyticsScenario.GroupByAsync);

    [Test, DependsOn(nameof(GroupBy), ProceedOnFailure = true)]
    public Task FilteredAggregate() => Run(AnalyticsScenario.FilteredAggregate, AnalyticsScenario.FilteredAggregateAsync);

    private Task Run(string caseId, Func<IAnalyticsDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Analytics, caseId, AnalyticsDrivers.Create, run);
}
