using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Refusal
{
    internal static async Task BothAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        NativeTextVectorFusionR40State state, SearchRequest request, CancellationToken token)
    {
        var sdk = await reader.Sdk.SearchAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue().Because(sdk.Problem?.Detail ?? string.Empty);
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(sdk.Value).IsNull();
        await NativeTextVectorFusionR40Pages.CurrentAsync(admin, state, token);
        _ = await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await NativeTextVectorFusionR40Pages.CurrentAsync(admin, state, token);
    }
}
