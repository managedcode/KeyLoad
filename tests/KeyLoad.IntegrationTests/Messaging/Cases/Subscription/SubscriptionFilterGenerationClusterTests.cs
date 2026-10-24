namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class SubscriptionFilterGenerationClusterTests
{
    [Test]
    public Task CompetingWorkersPausedFilterCasAndContiguousGapsSurviveTwoColdCutsWithOriginalReceipts()
        => SubscriptionFilterClusterTrial.RunAsync();
}
