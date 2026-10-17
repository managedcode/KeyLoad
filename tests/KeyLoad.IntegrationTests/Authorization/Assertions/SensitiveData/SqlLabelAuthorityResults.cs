using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SqlLabelAuthorityResults
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient session,
        PartitionRef partition, CancellationToken token)
    {
        var expected = new GraphSearchResult([
            new(new(new(partition, GraphSearchRf3Scenario.Documents, GraphSearchRf3Scenario.Alpha),
                McpCallerProtocol.FirstRevision, "{\"name\":\"alpha\"}", false, []), 1d / SqlLabelAuthorityProtocol.AlphaDenominator),
            new(new(new(partition, GraphSearchRf3Scenario.Documents, GraphSearchRf3Scenario.Beta),
                McpCallerProtocol.FirstRevision, "{\"name\":\"beta\"}", false, []), 1d / SqlLabelAuthorityProtocol.BetaDenominator),
            new(new(new(partition, GraphSearchRf3Scenario.Documents, GraphSearchRf3Scenario.Gamma),
                McpCallerProtocol.FirstRevision, "{\"name\":\"gamma\"}", false, []), 1d / SqlLabelAuthorityProtocol.GammaDenominator)]);
        await RoutesAsync(sdk, session, partition, SqlGraphSearchRf3Scenario.LabeledStatement, expected, token);
        await RoutesAsync(sdk, session, partition, SqlGraphSearchRf3Scenario.EmptyStatement, new([]), token);
    }

    private static async Task RoutesAsync(KeyLoadClient sdk, McpOfficialClient session, PartitionRef partition,
        string statement, GraphSearchResult expected, CancellationToken token)
    {
        var request = SqlGraphSearchRf3Scenario.Request(partition, statement);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await session.CallAsync(
            SqlGraphSearchRf3Scenario.QuerySearchTool, request, token));
        var q1 = SqlRf3Protocol.Call(partition, SqlGraphSearchRf3Scenario.QuerySearchTool, request);
        await SqlRf3Protocol.EqualAsync(expected, direct);
        await SqlRf3Protocol.EqualAsync(expected, official.Value);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<GraphSearchResult>(sdk, q1, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<GraphSearchResult>(session, q1, token));
        if (statement == SqlGraphSearchRf3Scenario.EmptyStatement)
        {
            await Assert.That(direct.Hits).IsEmpty();
            await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, official.Value);
        }
    }
}
