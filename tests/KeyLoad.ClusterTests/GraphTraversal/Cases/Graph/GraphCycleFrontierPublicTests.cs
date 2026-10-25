using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphCycleFrontierPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CyclesConvergingFrontiersAndDepthBoundsStayLiteralAcrossVisibilityRepairAndTwoClusterColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await GraphCycleFrontierPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
