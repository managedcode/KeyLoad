using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3SelectionRefusals
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        foreach (var objectValue in new[] { false, true })
        {
            var source = objectValue ? SafeProjectorOperationRf3SelectionCases.ObjectSource : SafeProjectorOperationRf3SelectionCases.ArraySource;
            var path = objectValue ? SafeProjectorOperationRf3SelectionCases.ObjectPath : SafeProjectorOperationRf3SelectionCases.ArrayPath;
            await RefuseAsync(root, reader, history,
                SafeProjectorOperationRf3SelectionCases.Sql(history.State.Partition, history.State.Collection,
                    SafeProjectorOperationRf3Protocol.First, source),
                SafeProjectorOperationRf3SelectionCases.Ast(history.State.Partition, history.State.Collection,
                    SafeProjectorOperationRf3Protocol.First, path), ErrorCode.UnsupportedCapability,
                SafeProjectorOperationRf3SelectionCases.UnsupportedScalar, SafeProjectorOperationRf3SelectionCases.UnsupportedScalar, token);
        }
        var request = SafeProjectorOperationRf3SelectionCases.Ast(history.State.Partition, history.State.Collection,
            SafeProjectorOperationRf3Protocol.First, SafeProjectorOperationRf3Protocol.LiteralPath);
        await RefuseAsync(root, reader, history,
            SafeProjectorOperationRf3SelectionCases.Sql(history.State.Partition, history.State.Collection,
                SafeProjectorOperationRf3Protocol.First, SafeProjectorOperationRf3SelectionCases.DuplicateSource),
            request with
            {
                Query = request.Query with
                {
                    Projection = [new(SafeProjectorOperationRf3Protocol.LiteralPath, SafeProjectorOperationRf3Protocol.Exposed),
                        new(SafeProjectorOperationRf3Protocol.NullPath, SafeProjectorOperationRf3Protocol.Exposed)]
                }
            }, ErrorCode.Validation, SafeProjectorOperationRf3SelectionCases.DuplicateSqlDetail,
            SafeProjectorOperationRf3SelectionCases.DuplicateAliases, token);
    }

    private static async Task RefuseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, QueryRequest sql, KeyLoad.Query.AstQueryRequest ast,
        ErrorCode code, string sqlDetail, string astDetail, CancellationToken token)
    {
        foreach (var typed in new[] { false, true })
        {
            var detail = typed ? astDetail : sqlDetail;
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var actual = typed ? await reader.Sdk.QueryAstAsync(ast, token) : await reader.Sdk.QueryAsync(sql, token);
                await SafeProjectorOperationRf3Errors.SdkAsync(actual, code, detail);
            }, token);
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(
                    typed ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute, typed ? (object)ast : sql, token),
                    code, history.State);
            }, token);
        }
    }
}
