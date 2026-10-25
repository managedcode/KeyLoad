using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;
using P = KeyLoad.ClusterTests.Authorization.SchemaVisibleProjectionClusterProtocol;

namespace KeyLoad.ClusterTests.Authorization;

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
                    await File.WriteAllTextAsync(Path.Combine(TestContext.ResultsDirectory, "schema-binding-request.json"),
                        JsonSerializer.Serialize(new { IsAst = ast, SqlRequest = request.Sql, AstRequest = request.Ast }, JsonDefaults.Options), token);
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
