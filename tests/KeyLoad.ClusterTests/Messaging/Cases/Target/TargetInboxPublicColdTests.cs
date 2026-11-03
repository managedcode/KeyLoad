namespace KeyLoad.ClusterTests.Messaging;

internal sealed class TargetInboxPublicColdTests
{
    [Test]
    public Task TargetPartitionInboxReceiptsRemainAtomicAndSourceAckSeparateThroughPolicyRefusalsTwoColdCutsAndHealthySdkMcpSql()
        => TargetInboxClusterTrial.RunAsync();
}
