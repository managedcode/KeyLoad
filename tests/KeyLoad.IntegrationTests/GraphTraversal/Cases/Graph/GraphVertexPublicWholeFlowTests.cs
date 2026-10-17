using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphVertexPublicWholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task CanonicalVerticesKeepAtomicRebindDeleteReplayAndBothAdjacencyDirectionsAcrossTwoRf3ColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await GraphVertexPublicFlow.Run(fixture, deadline.Token);
        await GraphCrossAuthorityR41Trial.RunAsync(fixture, deadline.Token);
    }
}
