using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphLabelFrontierPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task OrdinalLabelsChargeExcludedCandidatesAndRetainRevisionReplacementAcrossTwoClusterColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await GraphLabelFrontierPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
