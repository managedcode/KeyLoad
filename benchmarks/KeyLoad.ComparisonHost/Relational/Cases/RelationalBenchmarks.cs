using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>The relational cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class RelationalBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(RelationalScenario.Load, RelationalScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task PkSelect() => Run(RelationalScenario.PkSelect, RelationalScenario.PkSelectAsync);

    [Test, DependsOn(nameof(PkSelect), ProceedOnFailure = true)]
    public Task RangeSelect() => Run(RelationalScenario.RangeSelect, RelationalScenario.RangeSelectAsync);

    [Test, DependsOn(nameof(RangeSelect), ProceedOnFailure = true)]
    public Task Update() => Run(RelationalScenario.Update, RelationalScenario.UpdateAsync);

    [Test, DependsOn(nameof(Update), ProceedOnFailure = true)]
    public Task Delete() => Run(RelationalScenario.Delete, RelationalScenario.DeleteAsync);

    [Test, DependsOn(nameof(Delete), ProceedOnFailure = true)]
    public Task Mixed() => Run(RelationalScenario.Mixed, RelationalScenario.MixedAsync);

    private Task Run(string caseId, Func<IRelationalDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Relational, caseId, RelationalDrivers.Create, run);
}
