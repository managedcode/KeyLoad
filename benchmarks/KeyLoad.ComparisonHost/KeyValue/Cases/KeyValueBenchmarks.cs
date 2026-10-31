using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>The key-value cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class KeyValueBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(KeyValueScenario.Load, KeyValueScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task Get() => Run(KeyValueScenario.Get, KeyValueScenario.GetAsync);

    [Test, DependsOn(nameof(Get), ProceedOnFailure = true)]
    public Task Set() => Run(KeyValueScenario.Set, KeyValueScenario.SetAsync);

    [Test, DependsOn(nameof(Set), ProceedOnFailure = true)]
    public Task Delete() => Run(KeyValueScenario.Delete, KeyValueScenario.DeleteAsync);

    [Test, DependsOn(nameof(Delete), ProceedOnFailure = true)]
    public Task Mixed() => Run(KeyValueScenario.Mixed, KeyValueScenario.MixedAsync);

    private Task Run(string caseId, Func<IKeyValueDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.KeyValue, caseId, KeyValueDrivers.Create, run);
}
