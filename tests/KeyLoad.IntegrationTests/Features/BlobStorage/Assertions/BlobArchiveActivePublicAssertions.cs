using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicAssertions
{
    internal static async Task CurrentAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobArchiveActivePublicVersion version, CancellationToken token)
    {
        var head = version.Completed!.Value;
        var metadata = new BlobMetadataRequest(version.Begin.Blob);
        await SqlRf3Protocol.EqualAsync(head, await PublicReadAssertions.MetadataAsync(await sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlRf3Protocol.EqualAsync(head, (await PublicReadAssertions.RequiredMetadataAsync(
            await official.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
        for (var ordinal = 0; ordinal < version.Parts.Length; ordinal++)
        {
            var bytes = BlobArchiveActivePublicPublication.Bytes(ordinal, ordinal == version.Parts.Length - 1 ? 17 : 65536);
            await RangeAsync(sdk, official, head, ordinal * 65536L, bytes, token);
        }
        var edge = BlobArchiveActivePublicPublication.Bytes(0, 65536).AsSpan(65534, 2).ToArray()
            .Concat(BlobArchiveActivePublicPublication.Bytes(1, 2)).ToArray();
        await RangeAsync(sdk, official, head, 65534, edge, token);
        await RangeAsync(sdk, official, head, head.Length, [], token);
    }

    private static async Task RangeAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobMetadata head, long offset, byte[] bytes, CancellationToken token)
    {
        var request = new BlobReadRequest(head.Blob, head.Revision, offset, bytes.Length);
        var expected = new BlobReadResult(head, offset, bytes);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadBlobRangeAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await official.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }

    internal static async Task ActiveAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobArchiveActivePublicVersion active, bool restored, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(active.Begin.Blob, active.Begin.UploadId);
        var expected = active.Written.Last().Value with { Status = restored ? BlobUploadStatus.Aborted : BlobUploadStatus.Active };
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.UploadAsync(await sdk.GetBlobUploadInfoAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.RequiredUploadAsync(
            await official.CallAsync("keyload_blobs_upload_info", request, token))).Value);
        await Assert.That(expected.NextOrdinal).IsEqualTo(2);
        await Assert.That(expected.StoredBytes).IsEqualTo(131072L);
    }
}
