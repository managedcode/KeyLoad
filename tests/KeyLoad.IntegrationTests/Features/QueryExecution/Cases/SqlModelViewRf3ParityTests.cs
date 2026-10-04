using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-SQLVIEW-005: real SDK and official MCP clients read matching RF3 model views.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlModelViewRf3ParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSqlView005SqlAndAstSdkAndOfficialMcpMatchNativeRowsWithoutQueueEffects()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlModelViewRf3Scenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);

        await AssertEventParityAsync(sdk, mcp, scenario, deadline.Token);
        await AssertQueueParityWithoutEffectsAsync(sdk, mcp, scenario, deadline.Token);
    }

    private static async Task AssertEventParityAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(
            new(scenario.Stream), cancellationToken));
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            scenario.EventSql(), cancellationToken));
        var ast = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(
            scenario.EventAst(), cancellationToken));
        var mcpSql = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, scenario.EventSql(), cancellationToken));
        var mcpAst = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, scenario.EventAst(), cancellationToken));

        await SqlModelViewRf3ParityAssertions.AssertEventRowsAsync(native.Events, sql);
        await SqlModelViewRf3ParityAssertions.AssertEquivalentRowsAsync(sql, ast,
            mcpSql.Value, mcpAst.Value, "model-scan:events");
    }

    private static async Task AssertQueueParityWithoutEffectsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var before = await SqlModelViewRf3ParityAssertions.InspectBothAsync(sdk, scenario, cancellationToken);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            scenario.QueueSql(), cancellationToken));
        var ast = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(
            scenario.QueueAst(), cancellationToken));
        var mcpSql = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, scenario.QueueSql(), cancellationToken));
        var mcpAst = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, scenario.QueueAst(), cancellationToken));
        await SqlModelViewRf3ParityAssertions.AssertQueueRowsAsync(before, sql);
        await SqlModelViewRf3ParityAssertions.AssertEquivalentRowsAsync(sql, ast,
            mcpSql.Value, mcpAst.Value, "model-scan:queue-messages");
        await AssertExplainAsync(sdk, mcp, scenario, cancellationToken);
        var after = await SqlModelViewRf3ParityAssertions.InspectBothAsync(sdk, scenario, cancellationToken);
        await SqlModelViewRf3ParityAssertions.AssertQueueUnchangedAsync(before.First, after.First);
        await SqlModelViewRf3ParityAssertions.AssertQueueUnchangedAsync(before.Second, after.Second);
    }

    private static async Task AssertExplainAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var request = scenario.QueueSql() with { Sql = "EXPLAIN " + scenario.QueueSql().Sql };
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(request, cancellationToken));
        var mcpPage = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, request, cancellationToken));
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:queue-messages");
        await SqlRf3Protocol.EqualAsync(page.Rows, mcpPage.Value.Rows);
        using var json = System.Text.Json.JsonDocument.Parse(page.Rows[0].Json);
        await SqlModelViewRf3Assertions.AssertNoDeliveryAuthorityAsync(json.RootElement);
        await Assert.That(page.Rows[0].Json.Contains(SqlModelViewRf3Scenario.QueueCanary,
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(page.Rows[0].Json.Contains(SqlModelViewRf3Scenario.QueueHeaderCanary,
            StringComparison.Ordinal)).IsFalse();
    }
}
