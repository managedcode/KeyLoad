namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>AC-MSG-007: caller cancellation after a real first-lane RF3 claim.</summary>
[NotInParallel]
internal sealed class MultiLaneReceiveCancellationRf3Tests
{
    [Test]
    public Task SdkCancellationAfterFirstCommittedClaimReconcilesOriginalLaneIds()
        => MultiLaneReceiveCancellationScenario.RunAsync(false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OfficialMcpCancellationAfterFirstCommittedClaimReconcilesOriginalLaneIds()
        => MultiLaneReceiveCancellationScenario.RunAsync(true, TestContext.Current!.Execution.CancellationToken);
}
