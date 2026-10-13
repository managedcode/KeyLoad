using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRowCreatorPublicAssertions
{
    internal static async Task RefusedAsync<T>(Func<Task<Result<T>>> sdk, Func<Task<CallToolResult>> official,
        string credential, string privateValue, Func<Task> healthy)
    {
        var result = await sdk();
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await healthy();
        var mcp = await official();
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, credential, privateValue);
        await healthy();
    }

    internal static async Task ActiveAsync(RequestCqrsRf3Callers creator, BeginBlobUploadRequest begin,
        BlobUploadInfo expected, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(begin.Blob, begin.UploadId);
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.UploadAsync(
            await creator.Sdk.GetBlobUploadInfoAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await creator.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value.Upload);
        await Assert.That(await PublicReadAssertions.MetadataAsync(await creator.Sdk.GetBlobMetadataAsync(new(begin.Blob), token))).IsNull();
        await Assert.That((await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await creator.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(begin.Blob), token))).Value.Metadata).IsNull();
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers caller,
        BlobSupersessionPublicTrial.Version publication, CancellationToken token)
    {
        var metadata = publication.Completed.Value;
        await SqlRf3Protocol.EqualAsync(metadata, await PublicReadAssertions.MetadataAsync(
            await caller.Sdk.GetBlobMetadataAsync(new(metadata.Blob), token)));
        await SqlRf3Protocol.EqualAsync(metadata, (await PublicReadAssertions.RequiredMetadataAsync(
            await caller.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(metadata.Blob), token))).Value);
        foreach (var (offset, bytes) in new[] { (0L, BlobRowCreatorPublicPublication.Bytes), (metadata.Length, Array.Empty<byte>()) })
        {
            var request = new BlobReadRequest(metadata.Blob, metadata.Revision, offset, bytes.Length);
            var expected = new BlobReadResult(metadata, offset, bytes);
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.ReadBlobRangeAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await caller.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
        }
    }

    internal static async Task ReadDeniedAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers other, RequestCqrsRf3Callers creator,
        string credential, string privateValue, BlobSupersessionPublicTrial.Version publication, CancellationToken token)
    {
        var metadata = publication.Completed.Value;
        var outbox = await BlobPublishedRowReadFenceAssertions.BaselineAsync(administrator, creator, publication, token);
        Func<Task> healthy = () => BlobPublishedRowReadFenceAssertions.ObserveAsync(administrator, creator, publication, outbox, token);
        await RefusedAsync(() => other.Sdk.GetBlobMetadataAsync(new(metadata.Blob), token),
            () => other.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(metadata.Blob), token),
            credential, privateValue, healthy);
        foreach (var request in new[] { new BlobReadRequest(metadata.Blob, 1L, 0L, BlobRowCreatorPublicPublication.Bytes.Length),
            new BlobReadRequest(metadata.Blob, 1L, metadata.Length, 0) })
        {
            await RefusedAsync(() => other.Sdk.ReadBlobRangeAsync(request, token),
                () => other.Mcp.CallAsync("keyload_blobs_read_range", request, token), credential, privateValue, healthy);
        }
        await BlobPublishedRowReadFenceAssertions.RunAsync(administrator, other, creator, credential, privateValue, publication, outbox, token);
    }

    internal static async Task CreatorDeniedAsync(RequestCqrsRf3Callers other, RequestCqrsRf3Callers creator,
        string credential, string privateValue, BlobSupersessionPublicTrial.Version publication, CancellationToken token)
    {
        Func<Task> healthy = () => CurrentAsync(creator, publication, token);
        var info = new BlobUploadInfoRequest(publication.Begin.Blob, publication.Begin.UploadId);
        await RefusedAsync(() => other.Sdk.GetBlobUploadInfoAsync(info, token),
            () => other.Mcp.CallAsync("keyload_blobs_upload_info", info, token), credential, privateValue, healthy);
        var part = publication.Part with { CommandId = Guid.NewGuid() };
        await RefusedAsync(() => other.Sdk.WriteBlobPartAsync(part, token),
            () => other.Mcp.CallAsync("keyload_blobs_write_part", part, token), credential, privateValue, healthy);
        var complete = publication.Complete with { CommandId = Guid.NewGuid() };
        await RefusedAsync(() => other.Sdk.CompleteBlobUploadAsync(complete, token),
            () => other.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token), credential, privateValue, healthy);
    }
}
