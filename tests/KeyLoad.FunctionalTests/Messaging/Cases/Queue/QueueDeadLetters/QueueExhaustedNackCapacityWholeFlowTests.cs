namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueExhaustedNackCapacityWholeFlowTests
{
    [Test]
    public Task AcMsg088ExhaustedNackParkedSequenceMaximumImmutableFailureTwoColdRepairAndHealthy()
        => QueueExhaustedNackCapacityFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
