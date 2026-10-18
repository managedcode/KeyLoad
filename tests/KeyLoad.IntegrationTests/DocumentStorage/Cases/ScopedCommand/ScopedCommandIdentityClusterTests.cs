using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-DSTORE-009: real SDK and official MCP retries use complete atomic partition identity.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ScopedCommandIdentityClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task SamePrincipalCommandIdCommitsAndRecoversIndependentlyAcrossTwoPartitions()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ScopedCommandIdentityClusterScenario.CreateAsync(fixture, deadline.Token);
        await ScopedCommandIdentityClusterWorkflow.RunAsync(fixture, scenario, deadline.Token);
    }
}
