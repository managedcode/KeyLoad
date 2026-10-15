namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed class ReplicaSnapshotInstallTransferFencingTests
{
    [Test]
    public Task KilledAcknowledgedPrefixRefusesForeignTransferGapAndPrematureCompletionThenSameImageInstallsTailColdAndHealthy()
        => ReplicaSnapshotInstallFencingFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
