namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueDeadlineAutonomousClusterTests
{
    [Test]
    public Task ExistingScopedPrincipalRevokeColdRefusesDueThenRestoresAutonomousReadyAndOfficialClaimReceiptColdHealthy()
        => QueueDeadlineClusterTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task NullSelectedServicePreservesScheduledStateReceiptsAcrossColdThenPublicClaimAndHealthyAck()
        => NullQueueDeadlineClusterTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
