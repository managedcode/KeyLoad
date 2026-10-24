namespace KeyLoad.IntegrationTests.Search;

internal sealed class GraphSearchPathPolicyClusterTests
{
    [Test]
    public Task HiddenIntermediateStopsAllOperatorsUntilPersistedPolicyAndOwnerRepairSurviveReplayAndTwoCold()
        => GraphSearchPathPolicyClusterFlow.RunAsync();
}
