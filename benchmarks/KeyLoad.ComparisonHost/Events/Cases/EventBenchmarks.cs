using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>The event store cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class EventBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(EventScenario.Load, EventScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task Append() => Run(EventScenario.Append, EventScenario.AppendAsync);

    [Test, DependsOn(nameof(Append), ProceedOnFailure = true)]
    public Task Read() => Run(EventScenario.Read, EventScenario.ReadAsync);

    [Test, DependsOn(nameof(Read), ProceedOnFailure = true)]
    public Task Mixed() => Run(EventScenario.Mixed, EventScenario.MixedAsync);

    private Task Run(string caseId, Func<IEventDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Events, caseId, EventDrivers.Create, run);
}
