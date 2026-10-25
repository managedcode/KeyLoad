namespace KeyLoad.ClusterTests.Search;

internal sealed class GraphSearchPathPolicyClusterTests
{
    [Test]
    public Task HiddenIntermediateStopsAllOperatorsUntilPersistedPolicyAndOwnerRepairSurviveReplayAndTwoCold()
        => GraphSearchPathPolicyClusterFlow.RunAsync();
}
