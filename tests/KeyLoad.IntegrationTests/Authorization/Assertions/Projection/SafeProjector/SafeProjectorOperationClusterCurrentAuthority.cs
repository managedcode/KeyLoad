using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterCurrentAuthority
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var expected = new DocumentResult(history.State.First, history.Revision, history.SafeJson,
            true, SafeProjectorOperationClusterProtocol.RedactedPaths);
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(expected,
            await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token)));
        var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(history.State.First), token);
        await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationClusterProtocol.Canary);
        await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.QueryAsync(
            SafeProjectorOperationClusterRequests.UseSql(history.State, false), token),
            ErrorCode.PermissionDenied, SafeProjectorOperationClusterProtocol.UseDenied);
        await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst,
            SafeProjectorOperationClusterRequests.UseAst(history.State, false), token),
            ErrorCode.PermissionDenied, history.State);
    }
}
