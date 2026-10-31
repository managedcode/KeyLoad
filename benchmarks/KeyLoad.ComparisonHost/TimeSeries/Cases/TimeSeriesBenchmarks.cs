using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>The time-series cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class TimeSeriesBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(TimeSeriesScenario.Load, TimeSeriesScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task RangeRead() => Run(TimeSeriesScenario.RangeRead, TimeSeriesScenario.RangeReadAsync);

    [Test, DependsOn(nameof(RangeRead), ProceedOnFailure = true)]
    public Task Aggregate() => Run(TimeSeriesScenario.Aggregate, TimeSeriesScenario.AggregateAsync);

    [Test, DependsOn(nameof(Aggregate), ProceedOnFailure = true)]
    public Task Windows() => Run(TimeSeriesScenario.Windows, TimeSeriesScenario.WindowsAsync);

    private Task Run(string caseId, Func<ITimeSeriesDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.TimeSeries, caseId, TimeSeriesDrivers.Create, run);
}
