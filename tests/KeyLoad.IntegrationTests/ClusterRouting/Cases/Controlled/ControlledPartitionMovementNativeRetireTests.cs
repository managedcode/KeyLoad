namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original cancellation and graceful stop retain native Retire authority through two Aspire cold cohorts.</summary>
[NotInParallel]
internal sealed class ControlledPartitionMovementNativeRetireTests
{
    [Test]
    public Task OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners()
        => ControlledRetireAspireTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
