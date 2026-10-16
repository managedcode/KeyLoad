namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourceRetirementCleanupNativeTests
{
    [Test]
    public async Task NativeTableCleanupPagesRefuseBoundsRollbackThenResumeAcrossTwoJoinedColdOwners()
        => await ResourceIncarnationOwner.RunAsync(fixture => ResourceRetirementCleanupNativeFlow.RunAsync(
            fixture, TestContext.Current!.Execution.CancellationToken));
    [Test]
    public async Task NativeCandidateValidationKeepsOriginalFrameAndNoOpThenRejectsOversizeAndContinues()
        => await ResourceIncarnationOwner.RunAsync(ResourceRetirementCleanupCandidateNative.RunAsync);
    [Test]
    public async Task NativeSingleVisitPrefixPreservesFullExaminationChargesAndAtomicRollback()
        => await ResourceIncarnationOwner.RunAsync(ResourceRetirementCleanupPlanNative.RunAsync);
    [Test]
    public async Task PureCheckpointShapeRefusesMixedPartitionOrdinalAndCorruptRetainedReceiptWithoutHealing()
        => await ResourceIncarnationOwner.RunAsync(ResourceRetirementCleanupCheckpointRefusals.RunAsync);
}
