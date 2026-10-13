
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TenantCapacityPlacementRf3Tests
{
    [Test]
    public async Task OriginalSignedCapacityPlanPersistsAppliesReplaysAndSurvivesTwoColdOwnerCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3Scenario.RunAsync(linked.Token).ConfigureAwait(false);
    }
}
