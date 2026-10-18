using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaximumRangePublicReplay
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers callers, BlobMaximumRangePublicState state, CancellationToken token)
    {
        await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
        await BlobMaximumRangePublicAssertions.RefusalsAsync(callers, state, token);
        await SqlClusterProtocol.EqualAsync(state.Begun, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Begin, token)));
        await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
        await SqlClusterProtocol.EqualAsync(state.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Begin, token))).Value);
        await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
        for (var index = 0; index < state.Parts.Length; index++)
        {
            await SqlClusterProtocol.EqualAsync(state.Written[index], await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(state.Parts[index], token)));
            await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
            await SqlClusterProtocol.EqualAsync(state.Written[index], (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await callers.Mcp.CallAsync("keyload_blobs_write_part", state.Parts[index], token))).Value);
            await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
        }
        await SqlClusterProtocol.EqualAsync(state.Completed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token)));
        await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
        await SqlClusterProtocol.EqualAsync(state.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value);
        await BlobMaximumRangePublicAssertions.CurrentAsync(callers, state, token);
    }
}
