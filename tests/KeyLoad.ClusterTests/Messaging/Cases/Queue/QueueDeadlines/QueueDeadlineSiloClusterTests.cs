namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueDeadlineSiloClusterTests
{
    [Test]
    public Task OriginalSelectedQueueDeadlineSurvivesObservedSiloRestartRejoinAndTwoColdOwnersWithHealthyWork()
        => QueueDeadlineSiloClusterFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
