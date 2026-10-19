namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RequestDrainLifecycleTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task AcRequestDrain001JoinsOriginalProducerBeforeLowerStorageStageAndRetainsCanonicalOperation(
        bool canceledStop, bool callbackFailure)
        => await RequestDrainLifecycleFlow.RunAsync(canceledStop, callbackFailure, TestContext.Current!.Execution.CancellationToken);
}
