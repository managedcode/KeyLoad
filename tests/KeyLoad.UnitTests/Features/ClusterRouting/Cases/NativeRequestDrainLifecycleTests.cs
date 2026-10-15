namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeRequestDrainLifecycleTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task AcNativeRequestDrain001JoinsOriginalProducerBeforeLowerStorageStageAndRetainsCanonicalOperation(
        bool canceledStop, bool callbackFailure)
        => await NativeRequestDrainLifecycleFlow.RunAsync(canceledStop, callbackFailure, TestContext.Current!.Execution.CancellationToken);
}
