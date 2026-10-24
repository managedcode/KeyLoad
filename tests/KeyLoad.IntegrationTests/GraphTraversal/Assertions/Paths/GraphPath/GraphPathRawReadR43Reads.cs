using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphPathRawReadR43Reads
{
    internal static async Task StageAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRawReadR43State state, CancellationToken token)
    {
        foreach (var sql in new[] { false, true })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(sql
                ? await reader.Sdk.ShortestPathSqlAsync(state.Sql(), token)
                : await reader.Sdk.ShortestPathAsync(state.Request(), token));
            await ResultAsync(sdk, state);
            await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
            var reply = sql ? await reader.Mcp.CallAsync(GraphPathClusterScenario.SqlTool, state.Sql(), token)
                : await reader.Mcp.CallAsync(GraphPathClusterScenario.DirectTool, state.Request(), token);
            await ResultAsync((await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(reply)).Value, state);
            if (state.Allowed)
            {
                await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
                    .Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
            }
            else
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret,
                    GraphPathClusterScenario.SecretMarker);
            }
            await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
        }
    }

    private static async Task ResultAsync(GraphShortestPathResult result, GraphPathRawReadR43State state)
    {
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Hops).IsEqualTo(2);
        await SqlClusterProtocol.EqualAsync(state.StoredVertices, result.Vertices.ToArray());
        await SqlClusterProtocol.EqualAsync(state.StoredProjectedEdges, result.Edges.ToArray());
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }
}
