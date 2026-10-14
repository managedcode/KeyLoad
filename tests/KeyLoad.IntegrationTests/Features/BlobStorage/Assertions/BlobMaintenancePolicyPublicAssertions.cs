using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaintenancePolicyPublicAssertions
{
    internal static async Task PublishedAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published published, AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, published, token);
        foreach (var receipt in new[] { published.Begun.Receipt, published.Written[0].Receipt, published.Completed.Receipt })
        {
            await ReceiptAsync(published.Begin.Blob, placement, receipt);
        }
        await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, published.Begin.Blob,
            published.Begin.UploadId, published.Written[0].Value with
            {
                Status = BlobUploadStatus.Complete
            }, token);
        var eof = new BlobReadRequest(published.Begin.Blob, published.Completed.Value.Revision, published.Bytes.LongLength, 0);
        var expected = new BlobReadResult(published.Completed.Value, published.Bytes.LongLength, Array.Empty<byte>());
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(eof, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", eof, token))).Value);
    }

    internal static async Task ReceiptAsync(BlobRef blob, AtomicPartitionPlacementResolution placement,
        CommitReceipt receipt)
    {
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }

    internal static async Task RetiredAsync(RequestCqrsRf3Callers callers, BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.HeadAsync(callers, state.Tombstone, token);
        await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, state.Blob, state.Original.Begin.UploadId,
            state.Original.Written[0].Value with
            {
                Status = BlobUploadStatus.Complete
            }, token);
        var read = new BlobReadRequest(state.Blob, 2L, 0L, 1);
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await callers.Sdk.ReadBlobRangeAsync(read, token), ErrorCode.NotFound);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_read_range", read, token), ErrorCode.NotFound, dispatched: true);
    }

    internal static async Task FinalAsync(RequestCqrsRf3Callers callers, BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        await PublishedAsync(callers, state.Healthy, state.Placement, token);
        await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, state.Blob, state.Original.Begin.UploadId, null, token);
    }

    internal static async Task DeleteDeniedAsync(RequestCqrsRf3Callers callers, DeleteBlobRequest request,
        Func<Task> observe, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await callers.Sdk.DeleteBlobAsync(request, token), ErrorCode.PermissionDenied);
        await observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_delete", request, token), ErrorCode.PermissionDenied, dispatched: true);
        await observe();
    }

    internal static Task ReclaimDeniedAsync(RequestCqrsRf3Callers callers, BlobMaintenancePolicyPublicState state,
        Func<Task> observe, CancellationToken token)
        => BlobBoundedRetirementPublicAssertions.RefusedAsync(callers, state.DeniedReclaim, ErrorCode.PermissionDenied, observe, token);
}
