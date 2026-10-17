namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class GraphSearchPathPolicyRf3Tests
{
    [Test]
    public Task HiddenIntermediateStopsAllOperatorsUntilPersistedPolicyAndOwnerRepairSurviveReplayAndTwoCold()
        => GraphSearchPathPolicyRf3Flow.RunAsync();
}
