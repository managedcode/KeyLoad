using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Reads
{
    internal static async Task MatrixAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
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
                var reply = sql ? await reader.Mcp.CallAsync(GraphPathRf3Scenario.SqlTool, state.Sql(target), token)
                    : await reader.Mcp.CallAsync(GraphPathRf3Scenario.DirectTool, state.Request(target), token);
                await ResultAsync((await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(reply)).Value, state, target);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphPathRf3Scenario.SecretMarker);
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
        await SqlRf3Protocol.EqualAsync(vertices, result.Vertices.ToArray());
        await SqlRf3Protocol.EqualAsync(edges, result.Edges.ToArray());
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }
}
