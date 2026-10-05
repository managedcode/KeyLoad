using KeyLoad.IntegrationTests.Features.ClientApi;
using ModelContextProtocol.Protocol;

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
        var tool = await FindToolAsync(session, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(tool);
        await PartitionQueryMcpSchemaAssertions.VerifyAsync(tool.InputSchema, tool.OutputSchema!.Value);
    }

    private static async Task<Tool> FindToolAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        string? cursor = null;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        do
        {
            var page = await session.Client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, cancellationToken);
            count = checked(count + page.Tools.Count);
            await Assert.That(count).IsLessThanOrEqualTo(McpCallerProtocol.ToolCount);
            var matching = page.Tools.Where(value => value.Name == McpCallerTools.QueryPartitions).ToArray();
            await Assert.That(matching.Length).IsLessThanOrEqualTo(1);
            if (matching.Length == 1)
            { return matching[0]; }
            cursor = page.NextCursor;
            if (cursor is not null)
            { await Assert.That(cursors.Add(cursor)).IsTrue(); }
        } while (cursor is not null);
        throw new InvalidOperationException("The public partition-query tool is missing from official discovery.");
    }
}
