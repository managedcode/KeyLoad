using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedUploadIdentityPublicAssertions
{
    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers, BlobScopedUploadIdentityPublicState[] states, CancellationToken token)
    {
        foreach (var state in states)
        {
            var upload = new BlobUploadInfoRequest(state.Blob, state.Original.UploadId);
            await SqlClusterProtocol.EqualAsync(state.Current, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
            await SqlClusterProtocol.EqualAsync(state.Current, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value);
            var metadata = new BlobMetadataRequest(state.Blob);
            await SqlClusterProtocol.EqualAsync(state.Published?.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
            await SqlClusterProtocol.EqualAsync(state.Published?.Value, (await PublicReadAssertions.MetadataAsync(await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
            if (state.Published is { } published)
            {
                var read = new BlobReadRequest(state.Blob, 1L, 0L, 0);
                var expected = new BlobReadResult(published.Value, 0L, Array.Empty<byte>());
                await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(read, token)));
                await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", read, token))).Value);
            }
        }
    }

    internal static async Task BegunAsync(BlobScopedUploadIdentityPublicState state)
    {
        await Assert.That(state.Begun.Value.Blob).IsEqualTo(state.Blob);
        await Assert.That(state.Begun.Value.UploadId).IsEqualTo(state.Original.UploadId);
        await Assert.That(state.Begun.Value.ExpectedRevision).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.DeclaredLength).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.StoredBytes).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.NextOrdinal).IsEqualTo(0);
        await Assert.That(state.Begun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
        await ReceiptAsync(state.Begun.Receipt, state.Admitted.CommandId, state.Blob);
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt,
        Guid commandId, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
