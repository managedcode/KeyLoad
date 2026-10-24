using KeyLoad.Client;
using KeyLoad.IntegrationTests.Authorization;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Search;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>AC-GSEARCH-006: Q1 SQL graph search matches the public graph-search result across Orleans.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlGraphSearchClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGsearch006SqlSdkAndOfficialMcpMatchDirectMultiseedGraphSearch()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var sqlRequest = SqlGraphSearchClusterScenario.Request(scenario.Partition);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(sqlRequest, deadline.Token));
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(
            GraphSearchClusterScenario.Request(scenario.Partition), deadline.Token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            SqlGraphSearchClusterScenario.QuerySearchTool, sqlRequest, deadline.Token));

        await SqlGraphSearchClusterAssertions.AssertExpectedAsync(sql);
        await GraphSearchClusterAssertions.AssertEquivalentAsync(direct, sql);
        await GraphSearchClusterAssertions.AssertEquivalentAsync(sql, official.Value);
    }

    [Test]
    public async Task AcGsearch006SqlGraphLabelsUsePersistedFieldGrantAndEmptySetsStayEmpty()
    {
        using var deadline = McpCallerDeadline.Create();
        await SqlLabelAuthorityFlow.RunAsync(fixture, deadline.Token);
    }
}
