namespace KeyLoad.ClusterTests.Messaging;

internal sealed class SubscriptionFilterGenerationClusterTests
{
    [Test]
    public Task CompetingWorkersPausedFilterCasAndContiguousGapsSurviveTwoColdCutsWithOriginalReceipts()
        => SubscriptionFilterClusterTrial.RunAsync();
}
