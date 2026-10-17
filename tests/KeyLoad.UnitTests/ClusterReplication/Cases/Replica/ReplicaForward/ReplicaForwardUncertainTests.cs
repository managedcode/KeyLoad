namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardUncertainTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostAuthenticatedReplyAfterRealCommitPoisonsOnlyOriginalParentThenFreshReplayAndWriteSucceed(bool timeout)
        => await ReplicaForwardUncertainFlow.RunAsync(timeout, TestContext.Current!.Execution.CancellationToken);
}
