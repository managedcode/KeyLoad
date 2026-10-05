using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

/// <summary>AC-GRAPH-007/009: committed graph reads survive real RF3 leader loss and node restart.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathRf3LeaderLossTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGraph009CommittedPathMutationSurvivesLeaderLossAndRestoredNodeCatchup()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        _ = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        using var clients = new GraphPathRf3NodeClients(fixture, seed.Reader.Secret);
        await GraphPathRf3LeaderLoss.ExecuteAsync(fixture, seed, clients, deadline.Token);
    }
}
