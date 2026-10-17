namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class NativeResourceCommandBindingTests
{
    [Test]
    public Task ActualResourceFreeQueueAndBoundNativeCommandsRetainMetadataBeforeTighterReadsThenReplay()
        => NativeResourceCommandBindingFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
