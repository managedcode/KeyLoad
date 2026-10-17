namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueExhaustedNackCapacityWholeFlowTests
{
    [Test]
    public Task AcMsg088ExhaustedNativeNackParkedSequenceMaximumImmutableFailureTwoColdRepairAndHealthy()
        => QueueExhaustedNackCapacityFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
