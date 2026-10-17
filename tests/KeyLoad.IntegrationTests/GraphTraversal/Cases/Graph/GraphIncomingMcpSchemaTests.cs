using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphIncomingMcpSchemaTests(ClusterFixture fixture)
{
    [Test]
    public async Task OfficialClientDiscoversExactIncomingGraphSchemaAndHints()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var tool = await session.Client.DiscoverKeyLoadToolAsync(GraphIncomingMcpProtocol.Tool, deadline.Token);
        await NativeMcpSchemaEvidence.RetainIncomingGraphAsync(tool, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(tool);
        await GraphIncomingMcpSchemaAssertions.VerifyAsync(tool.InputSchema, tool.OutputSchema!.Value);
    }

}
