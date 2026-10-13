using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublicCancellationReplay
{
    internal static async Task PartAsync(RequestCqrsRf3Callers callers, WriteBlobPartRequest request,
        BlobCommitResult<BlobUploadInfo> expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.WriteBlobPartAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", request, token))).Value);
        await Assert.That(expected.Receipt.CommandId).IsEqualTo(request.CommandId);
    }

    internal static async Task CompleteAsync(RequestCqrsRf3Callers callers, CompleteBlobUploadRequest request,
        BlobCommitResult<BlobMetadata> expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.CompleteBlobUploadAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", request, token))).Value);
        await Assert.That(expected.Receipt.CommandId).IsEqualTo(request.CommandId);
    }

    internal static async Task OriginalAsync(RequestCqrsRf3Callers callers, BeginBlobUploadRequest begin,
        BlobCommitResult<BlobUploadInfo> begun, WriteBlobPartRequest part, BlobCommitResult<BlobUploadInfo> written,
        CompleteBlobUploadRequest complete, BlobCommitResult<BlobMetadata> completed, Func<Task> current,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(begun, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.BeginBlobUploadAsync(begin, token)));
        await current();
        await SqlRf3Protocol.EqualAsync(begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", begin, token))).Value);
        await current();
        await SqlRf3Protocol.EqualAsync(written, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.WriteBlobPartAsync(part, token)));
        await current();
        await SqlRf3Protocol.EqualAsync(written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", part, token))).Value);
        await current();
        await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.CompleteBlobUploadAsync(complete, token)));
        await current();
        await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value);
        await current();
    }
}
