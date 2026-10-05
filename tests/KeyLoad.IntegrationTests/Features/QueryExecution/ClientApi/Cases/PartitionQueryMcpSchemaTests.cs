using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryMcpSchemaTests(ClusterFixture fixture)
{
    [Test]
    public async Task OfficialClientDiscoversExactPartitionQuerySchemaAndHints()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var tool = await session.Client.DiscoverKeyLoadToolAsync(McpCallerTools.QueryPartitions, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(tool);
        await PartitionQueryMcpSchemaAssertions.VerifyAsync(tool.InputSchema, tool.OutputSchema!.Value);
    }

}
