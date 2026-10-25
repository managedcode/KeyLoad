namespace KeyLoad.ClusterTests.GraphTraversal;

[NotInParallel]
internal sealed class GraphLabelUseClusterTests
{
    [Test]
    public Task SdkMcpAndCanonicalSqlTraverseEnforceFreshLabelUseWithRepairReplayAndTwoCold()
        => GraphLabelUseClusterFlow.RunAsync();
}
