using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>The documents cases, run in order on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class DocumentBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Load() => Run(DocumentScenario.Load, DocumentScenario.LoadAsync);

    [Test, DependsOn(nameof(Load), ProceedOnFailure = true)]
    public Task PointRead() => Run(DocumentScenario.PointRead, DocumentScenario.PointReadAsync);

    [Test, DependsOn(nameof(PointRead), ProceedOnFailure = true)]
    public Task RangeQuery() => Run(DocumentScenario.RangeQuery, DocumentScenario.RangeQueryAsync);

    [Test, DependsOn(nameof(RangeQuery), ProceedOnFailure = true)]
    public Task Update() => Run(DocumentScenario.Update, DocumentScenario.UpdateAsync);

    [Test, DependsOn(nameof(Update), ProceedOnFailure = true)]
    public Task Delete() => Run(DocumentScenario.Delete, DocumentScenario.DeleteAsync);

    [Test, DependsOn(nameof(Delete), ProceedOnFailure = true)]
    public Task Mixed() => Run(DocumentScenario.Mixed, DocumentScenario.MixedAsync);

    private Task Run(string caseId, Func<IDocumentDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        => deployment.RunAsync(BenchmarkTypes.Documents, caseId, DocumentDrivers.Create, run);
}
