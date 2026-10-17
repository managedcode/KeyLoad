namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardUnverifiedTests
{
    [Test]
    public Task GenuineCommittedForwardWithTamperedReplyPoisonsOnlyOriginalParentAndFreshReplayDoesNotAppend()
        => ReplicaForwardUnverifiedFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
