namespace KeyLoad.IntegrationTests.Features.Authorization;

[NotInParallel]
internal sealed class InitialPrincipalEpochRf3Tests
{
    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(long.MinValue)]
    public Task RefusedInitialEpochThenRealSdkAndOfficialMcpKeyLifecycleSurviveTwoRf3ColdCycles(long epoch)
        => InitialPrincipalEpochRf3Flow.RunAsync(epoch);
}
