namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionOldCutStateMachineTests
{
    [Test]
    public Task RestoreOldGapFencesOriginalOutcomeResetRetainsInboxAndFreshAuthorityCompletesEveryInputAcrossTwoColdOwners()
        => SubscriptionOldCutFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
