namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CommandObservedPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task HeldOriginalReceiveUsesCurrentFieldOrHeaderPolicyThenExactReplayAndHealthyNewCommand(bool header)
        => CommandObservedPolicyFlow.RunAsync(header, TestContext.Current!.Execution.CancellationToken);
}
