using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-DSTORE-009: real SDK and official MCP retries use complete atomic partition identity.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ScopedCommandIdentityRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task SamePrincipalCommandIdCommitsAndRecoversIndependentlyAcrossTwoPartitions()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ScopedCommandIdentityRf3Scenario.CreateAsync(fixture, deadline.Token);
        await ScopedCommandIdentityRf3Workflow.RunAsync(fixture, scenario, deadline.Token);
    }
}
