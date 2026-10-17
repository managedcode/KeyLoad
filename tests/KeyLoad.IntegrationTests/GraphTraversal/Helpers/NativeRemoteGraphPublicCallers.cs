using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphPublicCallers
{
    internal static async Task<RemoteGraphEdgeResultV1> UpsertAndReplayAsync(KeyLoadClient sdk, McpOfficialClient official,
        UpsertRemoteGraphEdgeRequestV1 original, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.UpsertRemoteGraphEdgeAsync(original,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.CommandId).IsEqualTo(original.CommandId);
        var replay = (await McpCallerAssertions.SuccessAsync<RemoteGraphEdgeResultV1>(await official.CallAsync(
            RemoteGraphPublicProtocol.UpsertTool, original, cancellationToken).ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(actual, replay);
        return actual;
    }

    internal static async Task<RemoteGraphEdgeResultV1> DeleteAndReplayAsync(KeyLoadClient sdk, McpOfficialClient official,
        DeleteRemoteGraphEdgeRequestV1 original, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.DeleteRemoteGraphEdgeAsync(original,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.CommandId).IsEqualTo(original.CommandId);
        var replay = (await McpCallerAssertions.SuccessAsync<RemoteGraphEdgeResultV1>(await official.CallAsync(
            RemoteGraphPublicProtocol.DeleteTool, original, cancellationToken).ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(actual, replay);
        return actual;
    }

    internal static async Task<RemoteGraphEdgeResultV1> DeliverAndReplayAsync(KeyLoadClient sdk, McpOfficialClient official,
        DeliverRemoteGraphEdgeRequestV1 original, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.DeliverRemoteGraphEdgeAsync(original,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.CommandId).IsEqualTo(original.CommandId);
        var replay = (await McpCallerAssertions.SuccessAsync<RemoteGraphEdgeResultV1>(await official.CallAsync(
            RemoteGraphPublicProtocol.DeliverTool, original, cancellationToken).ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(actual, replay);
        return actual;
    }

    internal static async Task<(RemoteIncomingGraphEdgesPageV1 Sdk, RemoteIncomingGraphEdgesPageV1 Mcp)> IncomingParityAsync(
        KeyLoadClient sdk, McpOfficialClient official, ReadRemoteIncomingGraphEdgesRequestV1 original,
        CancellationToken cancellationToken)
    {
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.RemoteIncomingGraphEdgesAsync(original,
            cancellationToken).ConfigureAwait(false));
        var mcpPage = (await McpCallerAssertions.SuccessAsync<RemoteIncomingGraphEdgesPageV1>(await official.CallAsync(
            RemoteGraphPublicProtocol.IncomingTool, original, cancellationToken).ConfigureAwait(false))).Value;
        await Assert.That(sdkPage.Version).IsEqualTo(original.Version);
        await Assert.That(mcpPage.Version).IsEqualTo(original.Version);
        await Assert.That(sdkPage.Projection).IsEqualTo(mcpPage.Projection);
        await Assert.That(sdkPage.Rows.Length).IsEqualTo(mcpPage.Rows.Length);
        for (var index = 0; index < sdkPage.Rows.Length; index++)
        {
            await SqlRf3Protocol.EqualAsync(sdkPage.Rows[index].Edge, mcpPage.Rows[index].Edge);
            await Assert.That(sdkPage.Rows[index].DeliveredRevision).IsEqualTo(mcpPage.Rows[index].DeliveredRevision);
        }
        return (sdkPage, mcpPage);
    }
}
