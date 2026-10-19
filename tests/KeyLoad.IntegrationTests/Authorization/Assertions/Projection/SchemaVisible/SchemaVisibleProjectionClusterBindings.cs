using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Query;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionClusterBindings
{
    internal static async Task RefusedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        foreach (var request in SchemaVisibleProjectionClusterRequests.Restricted(history.State)
            .Concat(SchemaVisibleProjectionClusterJoins.Requests(history.State)))
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                {
                    var actual = ast ? await reader.Sdk.QueryAstAsync(request.Ast, token)
                        : await reader.Sdk.QueryAsync(request.Sql, token);
                    await SafeProjectorOperationClusterErrors.SdkAsync(actual, ErrorCode.Validation, P.BindingDetail);
                }, token);
                await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                {
                    await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(
                        ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute, ast ? (object)request.Ast : request.Sql, token),
                        ErrorCode.Validation, history.State);
                }, token);
            }
        }
        var star = SchemaVisibleProjectionClusterRequests.Star(history.State);
        var explain = star with
        {
            Query = star.Query with
            {
                Explain = true
            }
        };
        await ExplainAsync(root, reader, history, explain, token);
    }

    private static async Task ExplainAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, AstQueryRequest request, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.QueryAstAsync(request, token),
                ErrorCode.Validation, P.BindingDetail);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token),
                ErrorCode.Validation, history.State);
        }, token);
    }
}
