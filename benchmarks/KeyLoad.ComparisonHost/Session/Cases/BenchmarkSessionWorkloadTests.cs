using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Records distinct execution outcomes while every test uses the same live database.</summary>
[BenchmarkSessionSource]
[NotInParallel]
internal sealed class BenchmarkSessionWorkloadTests(BenchmarkSessionFixture fixture)
{
    [Test]
    public Task LoadMillion() => fixture.RunAsync(BenchmarkSessionFixture.LoadCase, static (session, token) => session.LoadAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(LoadMillion), ProceedOnFailure = true)]
    public Task BuildIndexes() => fixture.RunAsync(BenchmarkSessionFixture.IndexCase, static (session, token) => session.IndexAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(BuildIndexes), ProceedOnFailure = true)]
    public Task SequentialRead() => fixture.RunAsync(BenchmarkSessionFixture.SequentialReadCase, static (session, token) => session.SequentialReadAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(SequentialRead), ProceedOnFailure = true)]
    public Task RandomRead() => fixture.RunAsync(BenchmarkSessionFixture.RandomReadCase, static (session, token) => session.RandomReadAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(RandomRead), ProceedOnFailure = true)]
    public Task Search() => fixture.RunAsync(BenchmarkSessionFixture.SearchCase, static (session, token) => session.SearchAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(Search), ProceedOnFailure = true)]
    public Task Mixed() => fixture.RunAsync(BenchmarkSessionFixture.MixedCase, static (session, token) => session.MixedAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(Mixed), ProceedOnFailure = true)]
    public Task VerifyFinalState() => fixture.RunAsync(BenchmarkSessionFixture.VerifyCase, static (session, token) => session.VerifyAsync(token),
        TestContext.Current!.Execution.CancellationToken);

    [Test, DependsOn(nameof(VerifyFinalState), ProceedOnFailure = true)]
    public Task PrepareModels() => ModelAsync(BenchmarkSessionModelCases.Prepare);

    [Test, DependsOn(nameof(PrepareModels), ProceedOnFailure = true)]
    public Task VectorExact() => ModelAsync(BenchmarkSessionModelCases.Vector);

    [Test, DependsOn(nameof(VectorExact), ProceedOnFailure = true)]
    public Task QueueCycle() => ModelAsync(BenchmarkSessionModelCases.Queue);

    [Test, DependsOn(nameof(QueueCycle), ProceedOnFailure = true)]
    public Task StreamAppend() => ModelAsync(BenchmarkSessionModelCases.Append);

    [Test, DependsOn(nameof(StreamAppend), ProceedOnFailure = true)]
    public Task StreamRead() => ModelAsync(BenchmarkSessionModelCases.Read);

    [Test, DependsOn(nameof(StreamRead), ProceedOnFailure = true)]
    public Task StreamMixed() => ModelAsync(BenchmarkSessionModelCases.Mixed);

    private Task ModelAsync(string id) => fixture.RunAsync(id, (session, token) => session.ModelAsync(id, token),
        TestContext.Current!.Execution.CancellationToken);
}
