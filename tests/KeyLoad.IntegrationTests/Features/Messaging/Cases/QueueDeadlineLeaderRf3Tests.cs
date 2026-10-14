namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineLeaderRf3Tests
{
    [Test]
    public Task OriginalSelectedQueueDeadlineSurvivesObservedLeaderLossRejoinAndTwoColdOwnersWithHealthyWork()
        => QueueDeadlineLeaderRf3Flow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
