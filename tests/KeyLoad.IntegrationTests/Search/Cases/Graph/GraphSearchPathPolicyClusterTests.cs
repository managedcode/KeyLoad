namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class GraphSearchPathPolicyClusterTests
{
    [Test]
    public Task HiddenIntermediateStopsAllOperatorsUntilPersistedPolicyAndOwnerRepairSurviveReplayAndTwoCold()
        => GraphSearchPathPolicyClusterFlow.RunAsync();
}
