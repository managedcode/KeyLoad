namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class ResourceRetirementOriginal8ColdRecoveryTests
{
    [Test]
    public Task CleanColdReopenRecoversExactPersistedOriginalBeforeReservationSubmitReplayAndRecord()
        => ResourceRetirementOriginal8RecoveryFixture.RunAsync(ResourceRetirementOriginal8RecoveryFlow.ExecuteAsync);
}
