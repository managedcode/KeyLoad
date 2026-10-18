using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

/// <summary>AC-GRAPH-007/009: committed graph reads survive real Orleans silo loss and node restart.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathClusterSiloRestartTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGraph009CommittedPathMutationSurvivesSiloRestartAndRestoredNodeCatchup()
    {
        using var deadline = McpCallerDeadline.Create();
        {
            var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
            _ = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
            using var clients = new GraphPathClusterNodeClients(fixture, seed.Reader.Secret);
            await GraphPathClusterSiloRestart.ExecuteAsync(fixture, seed, clients, deadline.Token);
        }
        await GraphPathRebindR44Trial.RunAsync(fixture, deadline.Token);
    }
}
