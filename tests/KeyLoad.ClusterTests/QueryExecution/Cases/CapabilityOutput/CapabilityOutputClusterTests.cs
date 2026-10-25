namespace KeyLoad.ClusterTests.QueryExecution;

[NotInParallel]
internal sealed class CapabilityOutputClusterTests
{
    [Test]
    [Arguments(1)]
    public Task SdkAndOfficialMcpAdvertiseActualOutputBoundWithRefusalRepairAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputClusterFlow.RunAsync(mode);
}
