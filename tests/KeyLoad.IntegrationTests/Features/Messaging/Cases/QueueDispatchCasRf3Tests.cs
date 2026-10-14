namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDispatchCasRf3Tests
{
    [Test]
    public Task ExplicitQueueCasSdkOfficialMcpRefusalsReplayResumeTwoColdCutsAndHealthyAckPreservePoliciesAndCurrentState()
        => QueueDispatchCasRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
