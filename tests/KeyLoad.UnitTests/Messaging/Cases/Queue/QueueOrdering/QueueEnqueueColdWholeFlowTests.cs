namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueEnqueueColdWholeFlowTests
{
    [Test]
    public Task OriginalEnqueueImageAndReceiptSurviveColdReplayRefusalsThenAuthorizedHealthyContinuation()
        => QueueEnqueueColdTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
