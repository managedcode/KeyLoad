using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobEmptySlotPublicAssertions
{
    private const string Detail = "detail";
    private const string Capacity = "The blob reservation quota is exhausted.";
    private const string SafeCapacity = "The operation exceeds the available resource capacity.";
    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers, BlobEmptySlotPublicState state, CancellationToken token)
    {
        var upload = new BlobUploadInfoRequest(state.Blob, state.First.UploadId);
        await SqlClusterProtocol.EqualAsync(state.OriginalCurrent, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
        await SqlClusterProtocol.EqualAsync(state.OriginalCurrent, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value);
        if (state.Healthy is { } healthy)
        {
            var current = new BlobUploadInfoRequest(state.Blob, healthy.UploadId);
            await SqlClusterProtocol.EqualAsync(state.HealthyCurrent, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(current, token)));
            await SqlClusterProtocol.EqualAsync(state.HealthyCurrent, (await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", current, token))).Value);
        }
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

    internal static async Task RefusedAsync(RequestCqrsClusterCallers callers, BlobEmptySlotPublicState state,
        BeginBlobUploadRequest request, CancellationToken token)
    {
        var denied = await callers.Sdk.BeginBlobUploadAsync(request, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        await Assert.That(denied.Problem?.Detail).IsEqualTo(Capacity);
        await CurrentAsync(callers, state, token);
        var reply = await callers.Mcp.CallAsync("keyload_blobs_begin_upload", request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(Detail).GetString()).IsEqualTo(SafeCapacity);
        await CurrentAsync(callers, state, token);
        var upload = new BlobUploadInfoRequest(state.Blob, request.UploadId);
        if (request.UploadId != state.Healthy?.UploadId)
        {
            await Assert.That(await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token))).IsNull();
            await Assert.That((await PublicReadAssertions.UploadAsync(await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value).IsNull();
        }
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt,
        Guid commandId, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
