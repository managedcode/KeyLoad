using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobCompleteCasPublicAssertions
{
    private const string Detail = "detail";
    private const string Revision = "The blob head revision changed.";
    private const string SafeDetail = "The database operation could not be completed.";
    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers, BlobCompleteCasPublicUpload[] uploads,
        BlobCompleteCasPublicUpload? head, CancellationToken token)
    {
        var blob = uploads[0].Begin.Blob;
        var metadata = new BlobMetadataRequest(blob);
        await SqlClusterProtocol.EqualAsync(head?.Completed?.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlClusterProtocol.EqualAsync(head?.Completed?.Value, (await PublicReadAssertions.MetadataAsync(await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
        foreach (var upload in uploads)
        {
            var request = new BlobUploadInfoRequest(blob, upload.Begin.UploadId);
            await SqlClusterProtocol.EqualAsync(upload.Current, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(request, token)));
            await SqlClusterProtocol.EqualAsync(upload.Current, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value);
        }
        if (head?.Completed is { } published)
        {
            var read = new BlobReadRequest(blob, published.Value.Revision, 0L, head.Bytes.Length);
            var expected = new BlobReadResult(published.Value, 0L, head.Bytes);
            await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(read, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", read, token))).Value);
            var end = read with
            {
                Offset = head.Bytes.Length,
                Count = 0
            };
            var eof = new BlobReadResult(published.Value, head.Bytes.Length, Array.Empty<byte>());
            await SqlClusterProtocol.EqualAsync(eof, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(end, token)));
            await SqlClusterProtocol.EqualAsync(eof, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", end, token))).Value);
        }
    }

    internal static async Task RefusedAsync(RequestCqrsClusterCallers callers, CompleteBlobUploadRequest request,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload head, CancellationToken token)
    {
        var denied = await callers.Sdk.CompleteBlobUploadAsync(request, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        await Assert.That(denied.Problem?.Detail).IsEqualTo(Revision);
        await CurrentAsync(callers, uploads, head, token);
        var reply = await callers.Mcp.CallAsync("keyload_blobs_complete_upload", request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(Detail).GetString()).IsEqualTo(SafeDetail);
        await CurrentAsync(callers, uploads, head, token);
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id,
        BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
