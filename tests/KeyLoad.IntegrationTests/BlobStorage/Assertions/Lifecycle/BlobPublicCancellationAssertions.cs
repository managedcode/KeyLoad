using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublicCancellationAssertions
{
    internal static async Task StateAsync(RequestCqrsRf3Callers callers, BlobRef blob, BlobUploadInfo expected,
        CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(blob, expected.UploadId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetBlobUploadInfoAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value;
        await SqlRf3Protocol.EqualAsync(expected, sdk.Upload);
        await SqlRf3Protocol.EqualAsync(expected, mcp.Upload);
        var sdkMetadata = await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(new(blob), token));
        var mcpMetadata = (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(blob), token))).Value.Metadata;
        await Assert.That(sdkMetadata).IsNull();
        await Assert.That(mcpMetadata).IsNull();
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers, BlobMetadata metadata,
        byte[] bytes, CancellationToken token)
    {
        await Assert.That(metadata.Length).IsEqualTo((long)bytes.Length);
        await SqlRf3Protocol.EqualAsync(metadata, await PublicReadAssertions.MetadataAsync(
            await callers.Sdk.GetBlobMetadataAsync(new(metadata.Blob), token)));
        await SqlRf3Protocol.EqualAsync(metadata, (await PublicReadAssertions.RequiredMetadataAsync(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(metadata.Blob), token))).Value);
        var request = new BlobReadRequest(metadata.Blob, metadata.Revision, 0, bytes.Length);
        var expected = new BlobReadResult(metadata, 0, bytes);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }
}
