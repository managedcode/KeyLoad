using KeyLoad.Client;
using KeyLoad.ClusterTests.Authorization;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

/// <summary>AC-SQLVIEW-005: real SDK and official MCP clients read matching Orleans model views.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlModelViewClusterParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSqlView005SqlAndAstSdkAndOfficialMcpMatchRowsWithoutQueueEffects()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlModelViewClusterScenario.CreateAsync(sdk, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                fixture.AdminKey, deadline.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AssertEventParityAsync(sdk, mcp, scenario, deadline.Token);
                await AssertQueueParityWithoutEffectsAsync(sdk, mcp, scenario, deadline.Token);
            }, failures);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ModelRootClusterFlow.RunAsync(fixture, scenario.Partition,
            SqlModelViewClusterScenario.ExpectedEvents.Length, deadline.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertEventParityAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, CancellationToken cancellationToken)
    {
        var execution = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(
            new(scenario.Stream), cancellationToken));
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            scenario.EventSql(), cancellationToken));
        var ast = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(
            scenario.EventAst(), cancellationToken));
        var mcpSql = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, scenario.EventSql(), cancellationToken));
        var mcpAst = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, scenario.EventAst(), cancellationToken));

        await SqlModelViewClusterParityAssertions.AssertEventRowsAsync(execution.Events, sql);
        await SqlModelViewClusterParityAssertions.AssertEquivalentRowsAsync(sql, ast,
            mcpSql.Value, mcpAst.Value, "model-scan:events");
    }

    private static async Task AssertQueueParityWithoutEffectsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, CancellationToken cancellationToken)
    {
        var before = await SqlModelViewClusterParityAssertions.InspectBothAsync(sdk, scenario, cancellationToken);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            scenario.QueueSql(), cancellationToken));
        var ast = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(
            scenario.QueueAst(), cancellationToken));
        var mcpSql = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, scenario.QueueSql(), cancellationToken));
        var mcpAst = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, scenario.QueueAst(), cancellationToken));
        await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(before, sql);
        await SqlModelViewClusterParityAssertions.AssertEquivalentRowsAsync(sql, ast,
            mcpSql.Value, mcpAst.Value, "model-scan:queue-messages");
        await AssertExplainAsync(sdk, mcp, scenario, cancellationToken);
        var after = await SqlModelViewClusterParityAssertions.InspectBothAsync(sdk, scenario, cancellationToken);
        await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(before.First, after.First);
        await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(before.Second, after.Second);
    }

    private static async Task AssertExplainAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, CancellationToken cancellationToken)
    {
        var original = scenario.QueueAst();
        var request = original with
        {
            Query = original.Query with
            {
                Explain = true
            }
        };
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(request, cancellationToken));
        var mcpPage = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, request, cancellationToken));
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:queue-messages");
        await SqlClusterProtocol.EqualAsync(page.Rows, mcpPage.Value.Rows);
        using var json = System.Text.Json.JsonDocument.Parse(page.Rows[0].Json);
        await SqlModelViewClusterAssertions.AssertNoDeliveryAuthorityAsync(json.RootElement);
        await Assert.That(page.Rows[0].Json.Contains(SqlModelViewClusterScenario.QueueCanary,
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(page.Rows[0].Json.Contains(SqlModelViewClusterScenario.QueueHeaderCanary,
            StringComparison.Ordinal)).IsFalse();
    }
}
