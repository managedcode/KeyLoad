using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class ProjectionPublicCold
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        ProjectionPublicSettlement settled, CancellationToken token)
    {
        var state = settled.Original;
        var before = await ProjectionPublicOperations.ImageAsync(root, state, 2L, true, true, token);
        await ProjectionPublicAssertions.ExactAsync(state.SourceReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Source, token)));
        await ProjectionPublicAssertions.ExactAsync(state.SourceReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Source, token))).Value);
        await ProjectionPublicAssertions.ErrorAsync(caller, state.Complete, ErrorCode.PermissionDenied, token);
        await ProjectionPublicAssertions.ErrorAsync(caller, settled.CurrentReplay, ErrorCode.TokenInvalidated, token);
        await ProjectionPublicAssertions.ExactAsync(settled.Released,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReleaseProjectionAsync(settled.Release, token)));
        await ProjectionPublicAssertions.ExactAsync(settled.Released, (await McpCallerAssertions.SuccessAsync<ProjectionConsumerInfo>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsRelease, settled.Release, token))).Value);
        await ProjectionPublicAssertions.ExactAsync(settled.Purged,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PurgeOutboxAsync(settled.Purge, token)));
        await ProjectionPublicAssertions.ExactAsync(settled.Purged, (await McpCallerAssertions.SuccessAsync<OutboxHead>(
            await caller.Mcp.CallAsync(McpCallerTools.OutboxPurge, settled.Purge, token))).Value);
        await ProjectionPublicAssertions.ExactAsync(before,
            await ProjectionPublicOperations.ImageAsync(root, state, 2L, true, true, token));
    }
}
