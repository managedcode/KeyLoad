using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3CurrentAuthority
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        var expected = new DocumentResult(history.State.First, history.Revision, history.SafeJson,
            true, SafeProjectorOperationRf3Protocol.RedactedPaths);
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(expected,
            await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token)));
        var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(history.State.First), token);
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationRf3Protocol.Canary);
        await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.QueryAsync(
            SafeProjectorOperationRf3Requests.UseSql(history.State, false), token),
            ErrorCode.PermissionDenied, SafeProjectorOperationRf3Protocol.UseDenied);
        await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst,
            SafeProjectorOperationRf3Requests.UseAst(history.State, false), token),
            ErrorCode.PermissionDenied, history.State);
    }
}
