namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>AC-BSESSION-002/003/004/006: the execution client owns its workload against one selected live Aspire topology.</summary>
internal sealed class BenchmarkSessionTests
{
    [Test]
    public Task SessionRetainsMeasuredLoadOperationsMixedStateAndOriginalTestOutcomes()
        => BenchmarkSessionAspireFixture.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
