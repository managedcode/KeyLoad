using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-GSEARCH-006: Q1 SQL graph search matches the public graph-search result across RF3.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlGraphSearchRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGsearch006SqlSdkAndOfficialMcpMatchDirectMultiseedGraphSearch()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var sqlRequest = SqlGraphSearchRf3Scenario.Request(scenario.Partition);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(sqlRequest, deadline.Token));
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(
            GraphSearchRf3Scenario.Request(scenario.Partition), deadline.Token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            SqlGraphSearchRf3Scenario.QuerySearchTool, sqlRequest, deadline.Token));

        await SqlGraphSearchRf3Assertions.AssertExpectedAsync(sql);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, sql);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(sql, official.Value);
    }

    [Test]
    public async Task AcGsearch006SqlGraphLabelsUsePersistedFieldGrantAndEmptySetsStayEmpty()
    {
        using var deadline = McpCallerDeadline.Create();
        await SqlLabelAuthorityFlow.RunAsync(fixture, deadline.Token);
    }
}
