namespace KeyLoad.ClusterTests.Messaging;

internal sealed class TargetInboxPublicColdTests
{
    [Test]
    public Task TargetPartitionInboxReceiptsRemainAtomicAndSourceAckSeparateThroughPolicyRefusalsTwoColdCutsAndHealthySdkMcpQ1()
        => TargetInboxClusterTrial.RunAsync();
}
