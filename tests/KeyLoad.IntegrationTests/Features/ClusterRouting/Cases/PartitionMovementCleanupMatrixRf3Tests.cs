namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementCleanupMatrixRf3Tests
{
    [Test]
    [Arguments(PartitionMovementCleanupMatrixFaultRole.OtherKnownArm)]
    [Arguments(PartitionMovementCleanupMatrixFaultRole.ClaimedOwnerArm)]
    [Arguments(PartitionMovementCleanupMatrixFaultRole.RetainedObservedControl)]
    public Task ActualMissingAdmittedArmRefusesOriginalPublicationThenNewSessionSameDatabaseColdHealthy(
        PartitionMovementCleanupMatrixFaultRole role)
        => PartitionMovementCleanupMatrixRf3Trial.RunAsync(role, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ActualRetiredOtherResurrectionRefusesPublicationAfterReleasedAndLinkedObservedThenNewSessionColdHealthy()
        => PartitionMovementCleanupMatrixRf3Trial.RunAsync(PartitionMovementCleanupMatrixFaultRole.RetiredOtherResurrection,
            TestContext.Current!.Execution.CancellationToken);

}
