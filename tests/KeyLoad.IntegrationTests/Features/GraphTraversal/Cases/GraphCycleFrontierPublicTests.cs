using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphCycleFrontierPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CyclesConvergingFrontiersAndDepthBoundsStayLiteralAcrossVisibilityRepairAndTwoRf3ColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await GraphCycleFrontierPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
