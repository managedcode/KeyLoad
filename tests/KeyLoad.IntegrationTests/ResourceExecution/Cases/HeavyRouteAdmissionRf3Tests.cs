namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class HeavyRouteAdmissionRf3Tests
{
    [Test]
    public Task ActualHeldBodyRejectsFourHeavySdkMcpRoutesButKeepsControlThenFreshRepairReplayAndTwoCold()
        => HeavyRouteRf3Flow.RunAsync();
}
