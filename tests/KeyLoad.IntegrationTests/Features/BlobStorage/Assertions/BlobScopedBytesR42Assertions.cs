using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Assertions
{
    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        foreach (var state in states)
        {
            var upload = new BlobUploadInfoRequest(state.Blob, state.Begin.UploadId);
            await SqlRf3Protocol.EqualAsync(state.Current, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
            await SqlRf3Protocol.EqualAsync(state.Current, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value);
            var metadata = new BlobMetadataRequest(state.Blob);
            await SqlRf3Protocol.EqualAsync(state.Published?.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
            await SqlRf3Protocol.EqualAsync(state.Published?.Value, (await PublicReadAssertions.MetadataAsync(await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
            if (state.Published is not null)
            {
                foreach (var range in new (long Offset, int Count)[] { (0L, 4), (65534L, 6), (65536L, 17), (65553L, 0) })
                { await RangeAsync(callers, state, range.Offset, range.Count, token); }
            }
        }
    }
    private static async Task RangeAsync(RequestCqrsRf3Callers callers, BlobScopedBytesR42State state, long offset, int count, CancellationToken token)
    {
        var request = new BlobReadRequest(state.Blob, 1L, offset, count);
        var expected = new BlobReadResult(state.Published!.Value, offset, BlobScopedBytesR42Corpus.Bytes(state.Index, offset, count));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }
    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, BlobScopedBytesR42State state)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await SqlRf3Protocol.EqualAsync(new CommitToken(state.Placement.Incarnation, state.Blob.Partition.AtomicPartitionId, receipt.Token.Position, state.Placement.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
