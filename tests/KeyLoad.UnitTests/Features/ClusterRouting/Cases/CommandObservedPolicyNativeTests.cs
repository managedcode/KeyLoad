namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandObservedPolicyNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task HeldOriginalReceiveUsesCurrentFieldOrHeaderPolicyThenExactReplayAndHealthyNewCommand(bool header)
        => CommandObservedPolicyFlow.RunAsync(header, TestContext.Current!.CancellationToken);
}
