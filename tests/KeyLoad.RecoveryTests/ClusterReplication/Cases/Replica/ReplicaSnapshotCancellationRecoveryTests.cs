namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed class ReplicaSnapshotCancellationRecoveryTests
{
    [Test]
    public Task CancelledNativeBeginAndFullInstallRetainExactTransferThenSameOwnerTailTwoColdAndHealthyReplay()
        => ReplicaSnapshotCancellationRecoveryFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
