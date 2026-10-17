namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class CanonicalApplyNetworkIsolationTests
{
    [Test]
    public Task NativeRf3AppendProgressesDuringOwnedJournalFlushAndStableBatchReplaysExactly()
        => CanonicalApplyNetworkScenario.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
