namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Supporting genuine native owners; mandatory Aspire Docker evidence is separately retained.</summary>
[NotInParallel]
internal sealed class RemoteTransferPostAwaitNativeTests
{
    [Test]
    public Task GenuineBCommitThenARevokeReturnsUnknownAndSameIdReconcilesTwoColdOwnersBeforeFreshSdkMcpQ1()
        => RemoteTransferPostAwaitTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
