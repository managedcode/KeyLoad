namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeComparisonTests
{
    // AC-ISO-002/003/005/006: exactly one real engine/node/scenario cell per GitHub VM.
    [Test]
    public async Task SelectedNativeCaseRunsCommonIntensiveWorkload()
        => await IsolatedNativeCase.RunAsync(IsolatedNativeCaseIntent.ClosedLoop,
            TestContext.Current!.Execution.CancellationToken);
}
