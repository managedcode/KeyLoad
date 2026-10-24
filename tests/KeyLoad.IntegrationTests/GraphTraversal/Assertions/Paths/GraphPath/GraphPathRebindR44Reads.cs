using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphPathRebindR44Reads
{
    internal static async Task MatrixAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRebindR44State state, CancellationToken token)
    {
        foreach (var target in new[] { state.T, state.A })
        {
            foreach (var sql in new[] { false, true })
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(sql
                    ? await reader.Sdk.ShortestPathSqlAsync(state.Sql(target), token)
                    : await reader.Sdk.ShortestPathAsync(state.Request(target), token));
                await ResultAsync(sdk, state, target);
                await GraphPathRebindR44Current.ObserveAsync(admin, state, token);
                var reply = sql ? await reader.Mcp.CallAsync(GraphPathClusterScenario.SqlTool, state.Sql(target), token)
                    : await reader.Mcp.CallAsync(GraphPathClusterScenario.DirectTool, state.Request(target), token);
                await ResultAsync((await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(reply)).Value, state, target);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphPathClusterScenario.SecretMarker);
                await GraphPathRebindR44Current.ObserveAsync(admin, state, token);
            }
        }
    }

    private static async Task ResultAsync(GraphShortestPathResult result, GraphPathRebindR44State state, EntityRef target)
    {
        var vertices = target == state.T ? state.TargetPath : state.BranchPath;
        var edges = target == state.T ? state.TargetEdges : state.BranchEdges;
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsEqualTo(vertices.Length > 0);
        await Assert.That(result.Hops).IsEqualTo(vertices.Length == 0 ? 0 : vertices.Length - 1);
        await SqlClusterProtocol.EqualAsync(vertices, result.Vertices.ToArray());
        await SqlClusterProtocol.EqualAsync(edges, result.Edges.ToArray());
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }
}
