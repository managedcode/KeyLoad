namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeOpenLoopCancellationTests
{
    // AC-SCALE-020: the shared AppHost observes the actual native milestone, requests cancellation, and verifies post-cancel state.
    [Test]
    public async Task CancelsActualOpenLoopWorkAndReadsPersistedDocument()
        => await IsolatedNativeCase.RunAsync(IsolatedNativeCaseIntent.OpenLoopCancellationProof,
            TestContext.Current!.Execution.CancellationToken);
}
