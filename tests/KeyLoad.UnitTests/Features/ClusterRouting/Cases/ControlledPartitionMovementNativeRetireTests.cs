namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeRetireTests
{
    [Test]
    public Task OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners()
        => ControlledPartitionMovementNativeRetireTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
