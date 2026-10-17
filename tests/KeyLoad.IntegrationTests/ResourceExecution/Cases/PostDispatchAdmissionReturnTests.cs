namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class PostDispatchAdmissionReturnTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalPostDispatchCancelOrPhysicalDisconnectReturnsAdmissionThenSameGuidCommitReplayAndTwoCold(bool disconnect)
        => PostDispatchAdmissionReturnFlow.RunAsync(disconnect, TestContext.Current!.Execution.CancellationToken);
}
