using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathFieldUseR43Reads
{
    internal static async Task StageAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathFieldUseR43State state, CancellationToken token)
    {
        await HealthyAsync(admin, reader, state, false, token);
        if (state.Allowed)
        {
            await HealthyAsync(admin, reader, state, true, token);
        }
        else
        {
            await GraphPathFieldUseR43Policy.RefuseAsync(admin, reader, state, token);
        }
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathFieldUseR43State state, bool labeled, CancellationToken token)
    {
        foreach (var sql in new[] { false, true })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(sql
                ? await reader.Sdk.ShortestPathSqlAsync(state.Sql(labeled), token)
                : await reader.Sdk.ShortestPathAsync(state.Request(labeled), token));
            await ResultAsync(sdk, state);
            await GraphPathFieldUseR43Current.ObserveAsync(admin, state, token);
            var reply = sql ? await reader.Mcp.CallAsync(GraphPathClusterScenario.SqlTool, state.Sql(labeled), token)
                : await reader.Mcp.CallAsync(GraphPathClusterScenario.DirectTool, state.Request(labeled), token);
            await ResultAsync((await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(reply)).Value, state);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphPathClusterScenario.SecretMarker);
            await GraphPathFieldUseR43Current.ObserveAsync(admin, state, token);
        }
    }

    private static async Task ResultAsync(GraphShortestPathResult result, GraphPathFieldUseR43State state)
    {
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Hops).IsEqualTo(2);
        await SqlClusterProtocol.EqualAsync(state.Vertices, result.Vertices.ToArray());
        await SqlClusterProtocol.EqualAsync(state.ProjectedEdges, result.Edges.ToArray());
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }
}
