using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PhysicalShardNonRetiredPlanRf3Tests
{
    [Test]
    public Task ActualAbortedResumeYieldsOnceAndNeverAdmitsNextWholePartitionThenContinuesHealthyCold()
        => PhysicalShardPartitionMovementRf3FaultScenario.RunAsync(PartitionMovementParentPhaseRole.AcceptFence,
            TestContext.Current!.Execution.CancellationToken, PhysicalShardNonRetiredPlanRf3Recovery.ExecuteAsync);
}
