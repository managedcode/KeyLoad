namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class SubscriptionFilterGenerationClusterTests
{
    [Test]
    public Task CompetingWorkersPausedFilterCasAndContiguousGapsSurviveTwoColdCutsWithOriginalReceipts()
        => SubscriptionFilterClusterTrial.RunAsync();
}
