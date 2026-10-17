namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class TargetInboxPublicColdTests
{
    [Test]
    public Task TargetPartitionInboxReceiptsRemainAtomicAndSourceAckSeparateThroughPolicyRefusalsTwoColdCutsAndHealthySdkMcpQ1()
        => TargetInboxRf3Trial.RunAsync();
}
