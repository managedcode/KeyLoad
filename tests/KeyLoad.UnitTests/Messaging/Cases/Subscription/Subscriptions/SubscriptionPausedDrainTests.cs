namespace KeyLoad.UnitTests.Messaging;

internal sealed class SubscriptionPausedDrainTests
{
    [Test]
    public Task ManualPauseRejectsFreshClaimButDrainsCurrentLeasesAndResumesSameGenerationAcrossTwoColdOwners()
        => SubscriptionPausedDrainFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}

