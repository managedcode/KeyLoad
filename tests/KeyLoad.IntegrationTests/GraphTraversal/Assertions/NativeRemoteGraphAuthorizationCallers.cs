using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphAuthorizationCallers
{
    internal static async Task RequireUpsertDeniedAsync(RequestCqrsRf3Callers callers,
        UpsertRemoteGraphEdgeRequestV1 original, CancellationToken cancellationToken)
    {
        var sdk = await callers.Sdk.UpsertRemoteGraphEdgeAsync(original, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem).IsNotNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(sdk.Value).IsNull();
        _ = await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(RemoteGraphPublicProtocol.UpsertTool,
            original, cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
    }
}
