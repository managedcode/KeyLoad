namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>One exact pure, mixed or million-record native workload per isolated database runner.</summary>
internal sealed class DocumentNativeComparisonTests
{
    [Test]
    public async Task SelectedDocumentWorkloadUsesActualNativeClientsAndCompleteReadback()
        => await IsolatedNativeCase.RunAsync(IsolatedNativeCaseIntent.ClosedLoop,
            TestContext.Current!.Execution.CancellationToken);
}
