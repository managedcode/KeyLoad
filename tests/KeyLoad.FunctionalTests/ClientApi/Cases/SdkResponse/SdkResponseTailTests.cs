namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class SdkResponseTailTests
{
    [Test]
    public async Task CommittedReceiptPlusOneProbeRefusesBeforeOriginalHttpEofThenSameClientReplaysAndReadsHealthy()
    {
        var policy = SdkResponseTailOptions.Capture();
        using var timeout = new CancellationTokenSource(policy.Value.OperationTimeout, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        await SdkResponseTailFlow.RunAsync(caller.Token);
    }
}
