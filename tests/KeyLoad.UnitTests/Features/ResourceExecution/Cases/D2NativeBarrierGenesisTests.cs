namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class D2NativeBarrierGenesisTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public Task OriginalFirstNullBarrierPreservesProvenOrUnknownInventoryThroughReplayAndTwoColdOwners(
        bool secondAuthentication, bool firstCommand)
        => D2NativeBarrierGenesisFlow.RunAsync(secondAuthentication, firstCommand,
            TestContext.Current!.Execution.CancellationToken);
}
