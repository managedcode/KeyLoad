namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class TenantPlacementPolicyOutcomeTests
{
    [Test]
    public Task OriginalStalePolicyCasRetainsFullScopeOutcomeAcrossRepairReplayAndTwoColdCuts()
        => TenantPlacementPolicyOutcomeFlow.RunAsync();
}
