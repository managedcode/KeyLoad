namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class InboxProcessingPublicColdTests
{
    [Test]
    public async Task FailedEffectsOriginalInboxReceiptsAndRestoredPolicySurviveTwoClusterColdCutsThenHealthyProcessing()
        => await InboxProcessingClusterTrial.RunAsync();
}
