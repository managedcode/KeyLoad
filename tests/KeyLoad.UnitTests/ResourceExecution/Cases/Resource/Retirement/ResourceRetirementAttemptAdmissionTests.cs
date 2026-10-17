namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class ResourceRetirementAttemptAdmissionTests
{
    [Test]
    public Task DurableAttemptRetryPreservesOriginalIdentityAndHistoryBeforeExpiry()
        => ResourceRetirementPreparationValidationFixture.RunAsync(ResourceRetirementAttemptAdmissionFlow.ExecuteAsync);
}
