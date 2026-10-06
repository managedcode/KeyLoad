namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeOpenLoopComparisonTests
{
    // AC-SCALE-018..021: one AppHost-owned real target performs and verifies the exact fixed-rate cell.
    [Test]
    public async Task SelectedNativeOpenLoopCellRunsBoundedFixedRateWorkload()
        => await IsolatedNativeCase.RunAsync(IsolatedNativeCaseIntent.OpenLoopMeasured,
            TestContext.Current!.Execution.CancellationToken);
}
