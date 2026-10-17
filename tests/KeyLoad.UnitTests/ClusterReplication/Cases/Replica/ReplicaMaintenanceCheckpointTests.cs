namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaMaintenanceCheckpointTests
{
    /// <summary>AC-EMPTY-SNAPSHOT-MAINTENANCE-001: original maintenance cancellation joins actual native checkpoint IO.</summary>
    [Test]
    public Task NativeCheckpointPendingRemainsJoinedUntilOriginalFlushReturns()
        => ReplicaMaintenanceCheckpointTrial.RunAsync(failNativeFlush: false,
            TestContext.Current!.Execution.CancellationToken);

    /// <summary>AC-EMPTY-SNAPSHOT-MAINTENANCE-002: an actual native checkpoint fault fences state and survives original shutdown.</summary>
    [Test]
    public Task NativeCheckpointFailureFencesReadinessAndRetainsOriginalIOException()
        => ReplicaMaintenanceCheckpointTrial.RunAsync(failNativeFlush: true,
            TestContext.Current!.Execution.CancellationToken);
}
