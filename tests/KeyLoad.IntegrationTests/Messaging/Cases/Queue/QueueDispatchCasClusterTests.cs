namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDispatchCasClusterTests
{
    [Test]
    public Task ExplicitQueueCasSdkOfficialMcpRefusalsReplayResumeTwoColdCutsAndHealthyAckPreservePoliciesAndCurrentState()
        => QueueDispatchCasClusterTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
