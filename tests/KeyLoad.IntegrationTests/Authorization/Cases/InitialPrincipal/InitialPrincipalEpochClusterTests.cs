namespace KeyLoad.IntegrationTests.Authorization;

[NotInParallel]
internal sealed class InitialPrincipalEpochClusterTests
{
    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(long.MinValue)]
    public Task RefusedInitialEpochThenRealSdkAndOfficialMcpKeyLifecycleSurviveTwoClusterColdCycles(long epoch)
        => InitialPrincipalEpochClusterFlow.RunAsync(epoch);
}
