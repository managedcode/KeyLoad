namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class InboxProcessingPublicColdTests
{
    [Test]
    public async Task FailedEffectsOriginalInboxReceiptsAndRestoredPolicySurviveTwoRf3ColdCutsThenHealthyProcessing()
        => await InboxProcessingRf3Trial.RunAsync();
}
