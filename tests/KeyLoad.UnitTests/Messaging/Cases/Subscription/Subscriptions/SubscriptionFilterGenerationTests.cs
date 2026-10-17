namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionFilterGenerationTests
{
    [Test]
    public Task PausedFilterCasPreservesGapFencesBothWorkersAndColdOriginalReceipts()
        => SubscriptionFilterColdTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
