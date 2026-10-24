namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class CapabilityOutputClusterTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task SdkAndOfficialMcpAdvertiseActualOutputBoundWithRefusalRepairAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputClusterFlow.RunAsync(mode);
}
