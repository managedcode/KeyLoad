using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRawReadR43Reads
{
    internal static async Task StageAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphPathRawReadR43State state, CancellationToken token)
    {
        foreach (var sql in new[] { false, true })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(sql
                ? await reader.Sdk.ShortestPathSqlAsync(state.Sql(), token)
                : await reader.Sdk.ShortestPathAsync(state.Request(), token));
            await ResultAsync(sdk, state);
            await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
            var reply = sql ? await reader.Mcp.CallAsync(GraphPathRf3Scenario.SqlTool, state.Sql(), token)
                : await reader.Mcp.CallAsync(GraphPathRf3Scenario.DirectTool, state.Request(), token);
            await ResultAsync((await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(reply)).Value, state);
            if (state.Allowed)
            {
                await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
                    .Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
            }
            else
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret,
                    GraphPathRf3Scenario.SecretMarker);
            }
            await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
        }
    }

    private static async Task ResultAsync(GraphShortestPathResult result, GraphPathRawReadR43State state)
    {
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Hops).IsEqualTo(2);
        await SqlRf3Protocol.EqualAsync(state.Vertices, result.Vertices.ToArray());
        await SqlRf3Protocol.EqualAsync(state.ProjectedEdges, result.Edges.ToArray());
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }
}
