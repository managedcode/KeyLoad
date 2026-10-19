namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceCancellationTests
{
    [Test]
    public async Task AcScale016CancellationStopsAndJoinsTheOwnedProbe()
    {
        await ScaleServerResourceCancellationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
