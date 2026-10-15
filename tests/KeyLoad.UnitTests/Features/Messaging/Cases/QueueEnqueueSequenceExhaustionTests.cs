namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueEnqueueSequenceExhaustionTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task AcMsgEnqueueSequenceCeilingRetainsFailureAndColdImageThenExactRepairAndFreshProducer(bool strict, bool scheduled)
        => QueueEnqueueSequenceWholeFlow.RunAsync(strict, scheduled, TestContext.Current!.Execution.CancellationToken);
}
