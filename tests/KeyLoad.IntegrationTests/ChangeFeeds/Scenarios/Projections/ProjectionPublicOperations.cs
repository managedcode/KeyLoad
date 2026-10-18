using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class ProjectionPublicOperations
{
    internal static async Task<ProjectionBatchResult> CommitBothAsync(RequestCqrsClusterCallers caller,
        CommitProjectionBatchRequest request, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitProjectionAsync(request, token));
        await ProjectionPublicAssertions.ExactAsync(result, (await McpCallerAssertions.SuccessAsync<ProjectionBatchResult>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsCommit, request, token))).Value);
        return result;
    }

    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, PrincipalRecord principal, CancellationToken token)
        => await ProjectionPublicAssertions.ExactAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));

    internal static async Task<OutboxStatus> ImageAsync(RequestCqrsClusterCallers root, ProjectionPublicState state,
        long checkpoint, bool released, bool purged, CancellationToken token)
    {
        var status = await ProjectionPublicAssertions.PhaseAsync(root.Sdk, state, checkpoint, released, purged, token);
        await ProjectionPublicAssertions.ExactAsync(status, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, state.Partition, token))).Value);
        foreach (var item in new[] {
            new GetDocumentRequest(new(state.Partition, ProjectionPublicProtocol.Source, "input"), state.SourceReceipt.Token),
            new GetDocumentRequest(new(state.Partition, ProjectionPublicProtocol.Target, "effect"), state.Completed.Receipt.Token) })
        {
            var expected = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(item.Reference, item.MinimumToken!, token));
            await ProjectionPublicAssertions.ExactAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, item, token))).Value);
        }
        return status;
    }
}
