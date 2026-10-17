namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaRetainedReplayQuorumTests
{
    [Test]
    public Task ActualThreeVotersRetainBatchAndBlobReplayWithoutAppendThenPolicyAndQuorumRepairRemainHealthy()
        => ReplicaRetainedReplayFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
