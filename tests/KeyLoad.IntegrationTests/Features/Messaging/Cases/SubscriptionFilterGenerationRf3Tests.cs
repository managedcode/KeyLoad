namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class SubscriptionFilterGenerationRf3Tests
{
    [Test]
    public Task CompetingWorkersPausedFilterCasAndContiguousGapsSurviveTwoColdCutsWithOriginalReceipts()
        => SubscriptionFilterRf3Trial.RunAsync();
}
