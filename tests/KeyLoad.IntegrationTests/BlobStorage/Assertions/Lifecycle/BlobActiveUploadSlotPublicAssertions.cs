using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActiveUploadSlotPublicAssertions
{
    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        foreach (var state in states)
        {
            var upload = new BlobUploadInfoRequest(state.Blob, state.Original.UploadId);
            await SqlRf3Protocol.EqualAsync(state.Current, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
            await SqlRf3Protocol.EqualAsync(state.Current, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value);
            var metadata = new BlobMetadataRequest(state.Blob);
            await SqlRf3Protocol.EqualAsync(state.Published?.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
            await SqlRf3Protocol.EqualAsync(state.Published?.Value, (await PublicReadAssertions.MetadataAsync(await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
            if (state.Published is { } published)
            {
                var read = new BlobReadRequest(state.Blob, 1L, 0L, 0);
                var expected = new BlobReadResult(published.Value, 0L, Array.Empty<byte>());
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(read, token)));
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", read, token))).Value);
            }
        }
    }

    internal static async Task BegunAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state, CancellationToken token)
    {
        await Assert.That(state.Begun.Value.Blob).IsEqualTo(state.Blob);
        await Assert.That(state.Begun.Value.UploadId).IsEqualTo(state.Original.UploadId);
        await Assert.That(state.Begun.Value.ExpectedRevision).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.DeclaredLength).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.StoredBytes).IsEqualTo(0L);
        await Assert.That(state.Begun.Value.NextOrdinal).IsEqualTo(0);
        await Assert.That(state.Begun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
        await ReceiptAsync(callers, state.Begun.Receipt, state.Admitted.CommandId, state.Blob, token);
    }

    internal static async Task ReceiptAsync(RequestCqrsRf3Callers callers, CommitReceipt receipt,
        Guid commandId, BlobRef blob, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(new(1, blob.Partition), token));
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
