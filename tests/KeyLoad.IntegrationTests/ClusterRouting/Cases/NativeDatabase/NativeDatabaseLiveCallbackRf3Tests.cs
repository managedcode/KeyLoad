namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeDatabaseLiveCallbackRf3Tests
{
    [Test]
    public async Task ActualHeldNativeCallbackAcknowledgesBeforeAfterThenStableColdReceiptAndHealthyContinuation()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await NativeActivationLiveCallbackRf3Scenario.RunAsync(caller.Token).ConfigureAwait(false);
    }
}
