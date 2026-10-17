namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class CapabilityOutputRf3Tests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task SdkAndOfficialMcpAdvertiseActualOutputBoundWithRefusalRepairAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputRf3Flow.RunAsync(mode);
}
