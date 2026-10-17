using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementFencedAbortRf3Tests
{
    [Test]
    public Task OriginalCommittedFenceKillRollsBackThroughSdkMcpTwoColdCutsThenHealthyTransfer()
        => PhysicalShardPartitionMovementRf3FaultScenario.RunAsync(PartitionMovementParentPhaseRole.AcceptFence,
            TestContext.Current!.Execution.CancellationToken, PartitionMovementFencedAbortRf3Recovery.ExecuteAsync);
}
