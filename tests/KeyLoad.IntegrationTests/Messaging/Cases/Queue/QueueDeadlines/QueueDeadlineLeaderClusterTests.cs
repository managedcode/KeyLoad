namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineLeaderClusterTests
{
    [Test]
    public Task OriginalSelectedQueueDeadlineSurvivesObservedLeaderLossRejoinAndTwoColdOwnersWithHealthyWork()
        => QueueDeadlineLeaderClusterFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
