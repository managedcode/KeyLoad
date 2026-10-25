using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.Search;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SqlLabelAuthorityResults
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient session,
        GraphSearchClusterScenario scenario, CancellationToken token)
    {
        var partition = scenario.Partition;
        var expected = new GraphSearchResult([
            new(new(scenario.StoredVertex(GraphSearchClusterScenario.Documents, GraphSearchClusterScenario.Alpha),
                McpCallerProtocol.FirstRevision, "{\"name\":\"alpha\"}", false, []), 1d / SqlLabelAuthorityProtocol.AlphaDenominator),
            new(new(scenario.StoredVertex(GraphSearchClusterScenario.Documents, GraphSearchClusterScenario.Beta),
                McpCallerProtocol.FirstRevision, "{\"name\":\"beta\"}", false, []), 1d / SqlLabelAuthorityProtocol.BetaDenominator),
            new(new(scenario.StoredVertex(GraphSearchClusterScenario.Documents, GraphSearchClusterScenario.Gamma),
                McpCallerProtocol.FirstRevision, "{\"name\":\"gamma\"}", false, []), 1d / SqlLabelAuthorityProtocol.GammaDenominator)]);
        await RoutesAsync(sdk, session, partition, SqlGraphSearchClusterScenario.LabeledStatement, expected, token);
        await RoutesAsync(sdk, session, partition, SqlGraphSearchClusterScenario.EmptyStatement, new([]), token);
    }

    private static async Task RoutesAsync(KeyLoadClient sdk, McpOfficialClient session, PartitionRef partition,
        string statement, GraphSearchResult expected, CancellationToken token)
    {
        var request = SqlGraphSearchClusterScenario.Request(partition, statement);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await session.CallAsync(
            SqlGraphSearchClusterScenario.QuerySearchTool, request, token));
        var q1 = SqlClusterProtocol.Call(partition, SqlGraphSearchClusterScenario.QuerySearchTool, request);
        await SqlClusterProtocol.EqualAsync(expected, direct);
        await SqlClusterProtocol.EqualAsync(expected, official.Value);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<GraphSearchResult>(sdk, q1, token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<GraphSearchResult>(session, q1, token));
        if (statement == SqlGraphSearchClusterScenario.EmptyStatement)
        {
            await Assert.That(direct.Hits).IsEmpty();
            await GraphSearchClusterAssertions.AssertEquivalentAsync(direct, official.Value);
        }
    }
}
