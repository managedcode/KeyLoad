using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class QueryOnlyEpochR46Pages
{
    internal static async Task<QueryPage[]> RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryOnlyEpochR46State state, CancellationToken token)
    {
        var firstPages = new QueryPage[2];
        for (var route = 0; route < firstPages.Length; route++)
        {
            var before = await PolicyEpochPublicAssertions.ImageAsync(root.Sdk, state.Original.Scenario, token);
            var request = PolicyEpochPublicProtocol.Request(state.Original.Scenario.Partition);
            var first = await ReadAsync(reader, request, route, token);
            await RowAsync(first, ResourcePolicyUpdateClusterProtocol.DocumentId, ResourcePolicyUpdateClusterProtocol.SeedJson);
            await Assert.That(string.IsNullOrEmpty(first.Cursor)).IsFalse();
            firstPages[route] = first;
            await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
            var second = await ReadAsync(reader, request with
            {
                Cursor = first.Cursor
            }, 1 - route, token);
            await RowAsync(second, PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SecondJson);
            await Assert.That(second.Cursor).IsNull();
            await Assert.That(second.CutPosition).IsEqualTo(first.CutPosition);
            await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
            var after = await PolicyEpochPublicAssertions.ImageAsync(root.Sdk, state.Original.Scenario, token);
            await Assert.That(first.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
            await Assert.That(first.CutPosition).IsLessThanOrEqualTo(after.CutPosition);
        }
        return firstPages;
    }

    private static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader,
        QueryRequest request, int route, CancellationToken token)
        => route == 0 ? await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token))
            : (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                McpCallerTools.QueryExecute, request, token))).Value;

    private static async Task RowAsync(QueryPage page, string id, string json)
    {
        QueryRow[] rows = [new(id, 1L, json, false, [])];
        await SqlClusterProtocol.EqualAsync(rows, page.Rows.ToArray());
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }
}
