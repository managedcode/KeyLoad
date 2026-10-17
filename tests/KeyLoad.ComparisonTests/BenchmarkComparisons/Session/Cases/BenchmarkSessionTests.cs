namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BSESSION-002/003/004/006: the native test container shares one live Aspire topology.</summary>
internal sealed class BenchmarkSessionTests
{
    [Test]
    public Task NativeSessionRetainsMeasuredLoadOperationsMixedStateAndOriginalTestOutcomes()
        => BenchmarkSessionAspireFixture.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
