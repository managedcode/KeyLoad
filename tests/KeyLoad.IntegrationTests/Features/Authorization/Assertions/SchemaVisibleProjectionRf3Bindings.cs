using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Bindings
{
    internal static async Task RefusedAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        foreach (var request in SchemaVisibleProjectionRf3Requests.Restricted(history.State)
            .Concat(SchemaVisibleProjectionRf3Joins.Requests(history.State)))
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                {
                    var actual = ast ? await reader.Sdk.QueryAstAsync(request.Ast, token)
                        : await reader.Sdk.QueryAsync(request.Sql, token);
                    await SafeProjectorOperationRf3Errors.SdkAsync(actual, ErrorCode.Validation, P.BindingDetail);
                }, token);
                await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                {
                    await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(
                        ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute, ast ? (object)request.Ast : request.Sql, token),
                        ErrorCode.Validation, history.State);
                }, token);
            }
        }
        var star = SchemaVisibleProjectionRf3Requests.Star(history.State);
        var explain = star with
        {
            Query = star.Query with
            {
                Explain = true
            }
        };
        await ExplainAsync(root, reader, history, explain, token);
    }

    private static async Task ExplainAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, AstQueryRequest request, CancellationToken token)
    {
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.QueryAstAsync(request, token),
                ErrorCode.Validation, P.BindingDetail);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token),
                ErrorCode.Validation, history.State);
        }, token);
    }
}
