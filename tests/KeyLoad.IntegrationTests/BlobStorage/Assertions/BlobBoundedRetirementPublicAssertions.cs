using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobBoundedRetirementPublicAssertions
{
    internal static async Task FailedAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString())
            .Because(result.Problem?.Detail ?? string.Empty);
    }

    internal static async Task RefusedAsync(RequestCqrsRf3Callers callers, ReclaimBlobRequest request,
        ErrorCode expected, Func<Task> observe, CancellationToken token)
    {
        await FailedAsync(await callers.Sdk.ReclaimBlobAsync(request, token), expected);
        await observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_reclaim", request, token),
            expected, dispatched: true);
        await observe();
    }

    internal static async Task HeadAsync(RequestCqrsRf3Callers callers, BlobMetadata expected, CancellationToken token)
    {
        var request = new BlobMetadataRequest(expected.Blob);
        await SqlRf3Protocol.EqualAsync(expected,
            await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", request, token))).Value.Metadata);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published published, CancellationToken token)
    {
        await HeadAsync(callers, published.Completed.Value, token);
        for (var offset = 0; offset < published.Bytes.Length; offset += BlobLimits.MaxRangeBytes)
        {
            var count = Math.Min(BlobLimits.MaxRangeBytes, published.Bytes.Length - offset);
            var request = new BlobReadRequest(published.Begin.Blob, published.Completed.Value.Revision, offset, count);
            var expected = new BlobReadResult(published.Completed.Value, offset, published.Bytes.AsSpan(offset, count).ToArray());
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
        }
    }

    internal static async Task UploadAsync(RequestCqrsRf3Callers callers, BlobRef blob, Guid upload,
        BlobUploadInfo? expected, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(blob, upload);
        await SqlRf3Protocol.EqualAsync(expected,
            await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value.Upload);
    }

    internal static async Task LifetimeAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published old, ReclaimBlobRequest first,
        Func<Task> observe, CancellationToken token)
    {
        await FailedAsync(await callers.Sdk.BeginBlobUploadAsync(old.Begin, token), ErrorCode.TokenInvalidated);
        await observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", old.Begin, token),
            ErrorCode.TokenInvalidated, dispatched: true);
        await observe();
        foreach (var part in old.Parts)
        {
            await FailedAsync(await callers.Sdk.WriteBlobPartAsync(part, token), ErrorCode.TokenInvalidated);
            await observe();
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_write_part", part, token),
                ErrorCode.TokenInvalidated, dispatched: true);
            await observe();
        }
        await FailedAsync(await callers.Sdk.CompleteBlobUploadAsync(old.Complete, token), ErrorCode.TokenInvalidated);
        await observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", old.Complete, token),
            ErrorCode.TokenInvalidated, dispatched: true);
        await observe();
        await RefusedAsync(callers, first, ErrorCode.TokenInvalidated, observe, token);
    }
}
