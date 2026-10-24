namespace KeyLoad.UnitTests.Messaging;

internal sealed class SubscriptionOldCutStateMachineTests
{
    [Test]
    public Task RestoreOldGapFencesOriginalOutcomeResetRetainsInboxAndFreshAuthorityCompletesEveryInputAcrossTwoColdOwners()
        => SubscriptionOldCutFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
