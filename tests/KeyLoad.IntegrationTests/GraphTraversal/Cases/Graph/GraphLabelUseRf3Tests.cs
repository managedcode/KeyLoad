namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[NotInParallel]
internal sealed class GraphLabelUseRf3Tests
{
    [Test]
    public Task SdkMcpAndCanonicalSqlTraverseEnforceFreshLabelUseWithRepairReplayAndTwoCold()
        => GraphLabelUseRf3Flow.RunAsync();
}
