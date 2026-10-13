using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRejectedContentPublicReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, BlobSupersessionPublicTrial.Version value,
        WriteBlobPartRequest invalid, WriteBlobPartRequest changed, CompleteBlobUploadRequest wrong, CancellationToken token)
    {
        await BlobRejectedContentPublicAssertions.PartRefusedAsync(callers, invalid, ErrorCode.Validation,
            () => BlobRejectedContentPublicState.CurrentAsync(callers, value, token), token);
        await BlobRejectedContentPublicAssertions.PartRefusedAsync(callers, changed, ErrorCode.Conflict,
            () => BlobRejectedContentPublicState.CurrentAsync(callers, value, token), token);
        await BlobRejectedContentPublicAssertions.CompleteRefusedAsync(callers, wrong,
            () => BlobRejectedContentPublicState.CurrentAsync(callers, value, token), token);
        await SuccessfulAsync(callers, value, token);
    }

    private static async Task SuccessfulAsync(RequestCqrsRf3Callers callers,
        BlobSupersessionPublicTrial.Version value, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(value.Begun,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(value.Begin, token)));
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
        await SqlRf3Protocol.EqualAsync(value.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", value.Begin, token))).Value);
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
        await SqlRf3Protocol.EqualAsync(value.Written,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(value.Part, token)));
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
        await SqlRf3Protocol.EqualAsync(value.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", value.Part, token))).Value);
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
        await SqlRf3Protocol.EqualAsync(value.Completed,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(value.Complete, token)));
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
        await SqlRf3Protocol.EqualAsync(value.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", value.Complete, token))).Value);
        await BlobRejectedContentPublicState.CurrentAsync(callers, value, token);
    }
}
