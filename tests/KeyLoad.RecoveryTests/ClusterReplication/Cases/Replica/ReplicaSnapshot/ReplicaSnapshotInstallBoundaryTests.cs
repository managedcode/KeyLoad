namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

/// <summary>Actual original install admission failure followed by the same transfer and cold retained-tail recovery.</summary>
internal sealed class ReplicaSnapshotInstallBoundaryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualSnapshotCeilingOrOriginalCancelledInstallPreservesWholeCutThenSameTransferAndColdTailAreHealthy(bool canceled)
        => ReplicaSnapshotInstallBoundaryFlow.RunAsync(canceled, TestContext.Current!.Execution.CancellationToken);
}
