using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathFieldUseR43Policy
{
    private const string DetailKey = "detail";
    private const string SdkDetail = "The query requires a protected field-use grant.";
    private const string McpDetail = "The authenticated principal is not permitted to perform this operation.";

    internal static async Task RefuseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathFieldUseR43State state, CancellationToken token)
    {
        foreach (var sql in new[] { false, true })
        {
            var result = sql ? await reader.Sdk.ShortestPathSqlAsync(state.Sql(true), token)
                : await reader.Sdk.ShortestPathAsync(state.Request(true), token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, SdkDetail), result.Problem);
            await GraphPathFieldUseR43Current.ObserveAsync(admin, state, token);
            await GraphPathFieldUseR43Reads.HealthyAsync(admin, reader, state, false, token);
            var reply = sql ? await reader.Mcp.CallAsync(GraphPathClusterScenario.SqlTool, state.Sql(true), token)
                : await reader.Mcp.CallAsync(GraphPathClusterScenario.DirectTool, state.Request(true), token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(DetailKey).GetString()).IsEqualTo(McpDetail);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, GraphPathClusterScenario.SecretMarker);
            await GraphPathFieldUseR43Current.ObserveAsync(admin, state, token);
            await GraphPathFieldUseR43Reads.HealthyAsync(admin, reader, state, false, token);
        }
    }
}
