using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Full public SDK and official MCP authorization outcome and document-value assertions.</summary>
internal static class FunctionalGateUserAssertions
{
    private const string McpPermissionDenied = "The authenticated principal is not permitted to perform this operation.";

    internal static async Task EqualDocumentAsync(FunctionalGateContext context, RequestCqrsClusterCallers caller,
        EntityRef reference, DocumentReadResult expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(caller.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(caller,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    internal static async Task RefusedReadAsync(RequestCqrsClusterCallers caller, EntityRef reference, CancellationToken token)
    {
        var sdk = await caller.Sdk.GetAsync(reference, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), token), ErrorCode.PermissionDenied, dispatched: true);
    }

    internal static async Task RefusedWriteAsync(RequestCqrsClusterCallers caller, CommandRequest command,
        string? detail, CancellationToken token)
    {
        var sdk = await caller.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        var mcp = await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        if (detail is not null)
        {
            await Assert.That(sdk.Problem!.Detail).IsEqualTo(detail);
            await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpPermissionDenied);
        }
    }
}
