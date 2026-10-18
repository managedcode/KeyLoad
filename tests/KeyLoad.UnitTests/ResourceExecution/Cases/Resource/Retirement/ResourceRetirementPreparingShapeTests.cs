namespace KeyLoad.UnitTests.Features.ResourceExecution;

// Native generated DTO and private shape boundary tests; no live membership, transport or CLUSTER qualification claim.
internal sealed class ResourceRetirementPreparingShapeTests
{
    [Test]
    public Task PreparationOwnerStatusGenericAttemptsAndIssueRequireClosedReadArmsWithZeroEpochUnassigned()
        => ResourceRetirementPreparingReadShapeFlow.RequestsAsync();

    [Test]
    public Task UnknownAttemptAndIssueNeverBecomeAccountedAbsenceAndCrossArmRepliesAreRefused()
        => ResourceRetirementPreparingReadShapeFlow.RepliesAsync();

    [Test]
    public Task OriginalCommittedPrepareAndRecordReservationResultsRejectCrossArmsThenRemainHealthy()
        => ResourceRetirementPreparingResultShapeFlow.RunAsync();
}
