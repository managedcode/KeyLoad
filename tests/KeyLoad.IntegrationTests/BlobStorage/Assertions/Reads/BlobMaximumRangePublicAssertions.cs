using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaximumRangePublicAssertions
{
    private const string Detail = "detail";
    private const string Invalid = "The blob request or policy is invalid.";
    private const string SafeInvalid = "The database operation could not be completed.";

    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers, BlobMaximumRangePublicState state, CancellationToken token)
    {
        var metadata = new BlobMetadataRequest(state.Begin.Blob);
        await SqlClusterProtocol.EqualAsync(state.Completed.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlClusterProtocol.EqualAsync(state.Completed.Value, (await PublicReadAssertions.RequiredMetadataAsync(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
        var upload = new BlobUploadInfoRequest(state.Begin.Blob, state.Begin.UploadId);
        await SqlClusterProtocol.EqualAsync(state.CurrentUpload, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
        await SqlClusterProtocol.EqualAsync(state.CurrentUpload, (await PublicReadAssertions.RequiredUploadAsync(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", upload, token))).Value);
        foreach (var offset in new long[] { 0L, 1L, 65535L, 65536L, 65537L })
        {
            await RangeAsync(callers, state, offset, 65536, token);
        }
        await RangeAsync(callers, state, 131071L, 2, token);
        await RangeAsync(callers, state, 131073L, 0, token);
    }

    private static async Task RangeAsync(RequestCqrsClusterCallers callers, BlobMaximumRangePublicState state,
        long offset, int count, CancellationToken token)
    {
        var request = new BlobReadRequest(state.Begin.Blob, 1L, offset, count);
        var expected = new BlobReadResult(state.Completed.Value, offset, BlobMaximumRangePublicPublication.Bytes(offset, count));
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }

    internal static async Task RefusalsAsync(RequestCqrsClusterCallers callers, BlobMaximumRangePublicState state, CancellationToken token)
    {
        foreach (var request in new[]
        {
            new BlobReadRequest(state.Begin.Blob, 1L, 0L, 65537),
            new BlobReadRequest(state.Begin.Blob, 1L, 65538L, 65536)
        })
        {
            var result = await callers.Sdk.ReadBlobRangeAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
            await Assert.That(result.Problem?.Detail).IsEqualTo(Invalid);
            await CurrentAsync(callers, state, token);
            var reply = await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(Detail).GetString()).IsEqualTo(SafeInvalid);
            await CurrentAsync(callers, state, token);
        }
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id, BlobMaximumRangePublicState state)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Begin.Blob.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Mutations).IsEmpty();
    }
}
