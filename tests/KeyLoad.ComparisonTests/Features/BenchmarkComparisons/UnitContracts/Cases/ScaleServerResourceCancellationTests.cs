namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceCancellationTests
{
    [Test]
    public async Task AcScale016CancellationStopsAndJoinsTheOwnedNativeProbe()
    {
        await ScaleServerResourceCancellationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
