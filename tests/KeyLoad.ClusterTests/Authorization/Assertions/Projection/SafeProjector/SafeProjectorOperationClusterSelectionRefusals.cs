using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterSelectionRefusals
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        foreach (var objectValue in new[] { false, true })
        {
            var source = objectValue ? SafeProjectorOperationClusterSelectionCases.ObjectSource : SafeProjectorOperationClusterSelectionCases.ArraySource;
            var path = objectValue ? SafeProjectorOperationClusterSelectionCases.ObjectPath : SafeProjectorOperationClusterSelectionCases.ArrayPath;
            await RefuseAsync(root, reader, history,
                SafeProjectorOperationClusterSelectionCases.Sql(history.State.Partition, history.State.Collection,
                    SafeProjectorOperationClusterProtocol.First, source),
                SafeProjectorOperationClusterSelectionCases.Ast(history.State.Partition, history.State.Collection,
                    SafeProjectorOperationClusterProtocol.First, path), ErrorCode.UnsupportedCapability,
                SafeProjectorOperationClusterSelectionCases.UnsupportedScalar, SafeProjectorOperationClusterSelectionCases.UnsupportedScalar, token);
        }
        var request = SafeProjectorOperationClusterSelectionCases.Ast(history.State.Partition, history.State.Collection,
            SafeProjectorOperationClusterProtocol.First, SafeProjectorOperationClusterProtocol.LiteralPath);
        await RefuseAsync(root, reader, history,
            SafeProjectorOperationClusterSelectionCases.Sql(history.State.Partition, history.State.Collection,
                SafeProjectorOperationClusterProtocol.First, SafeProjectorOperationClusterSelectionCases.DuplicateSource),
            request with
            {
                Query = request.Query with
                {
                    Projection = [new(SafeProjectorOperationClusterProtocol.LiteralPath, SafeProjectorOperationClusterProtocol.Exposed),
                        new(SafeProjectorOperationClusterProtocol.NullPath, SafeProjectorOperationClusterProtocol.Exposed)]
                }
            }, ErrorCode.Validation, SafeProjectorOperationClusterSelectionCases.DuplicateSqlDetail,
            SafeProjectorOperationClusterSelectionCases.DuplicateAliases, token);
    }

    private static async Task RefuseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, QueryRequest sql, KeyLoad.Query.AstQueryRequest ast,
        ErrorCode code, string sqlDetail, string astDetail, CancellationToken token)
    {
        foreach (var typed in new[] { false, true })
        {
            var detail = typed ? astDetail : sqlDetail;
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var actual = typed ? await reader.Sdk.QueryAstAsync(ast, token) : await reader.Sdk.QueryAsync(sql, token);
                await SafeProjectorOperationClusterErrors.SdkAsync(actual, code, detail);
            }, token);
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(
                    typed ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute, typed ? (object)ast : sql, token),
                    code, history.State);
            }, token);
        }
    }
}
