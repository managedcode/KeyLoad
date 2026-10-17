using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphLabelFrontierPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task OrdinalLabelsChargeExcludedCandidatesAndRetainRevisionReplacementAcrossTwoRf3ColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await GraphLabelFrontierPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
