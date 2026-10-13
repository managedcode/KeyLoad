using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSupersessionPublicAssertions
{
    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers,
        BlobSupersessionPublicTrial.Version value, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(value.Begun,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(value.Begin, token)));
        await SqlRf3Protocol.EqualAsync(value.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", value.Begin, token))).Value);
        await SqlRf3Protocol.EqualAsync(value.Written,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(value.Part, token)));
        await SqlRf3Protocol.EqualAsync(value.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", value.Part, token))).Value);
        await SqlRf3Protocol.EqualAsync(value.Completed,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(value.Complete, token)));
        await SqlRf3Protocol.EqualAsync(value.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", value.Complete, token))).Value);
        foreach (var receipt in new[] { value.Begun.Receipt, value.Written.Receipt, value.Completed.Receipt })
        {
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(value.Begin.Blob.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        }
        await Assert.That(value.Begun.Receipt.CommandId).IsEqualTo(value.Begin.CommandId);
        await Assert.That(value.Written.Receipt.CommandId).IsEqualTo(value.Part.CommandId);
        await Assert.That(value.Completed.Receipt.CommandId).IsEqualTo(value.Complete.CommandId);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers,
        BlobSupersessionPublicTrial.Version current, CancellationToken token)
    {
        var metadata = new BlobMetadataRequest(current.Begin.Blob);
        await SqlRf3Protocol.EqualAsync(current.Completed.Value,
            await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlRf3Protocol.EqualAsync(current.Completed.Value, (await PublicReadAssertions.RequiredMetadataAsync(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
        var request = new BlobReadRequest(current.Begin.Blob, 2, 0, current.Part.Bytes.Length);
        var expected = new BlobReadResult(current.Completed.Value, 0, current.Part.Bytes);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }

    internal static async Task StaleAsync(RequestCqrsRf3Callers callers,
        BlobSupersessionPublicTrial.Version first, CancellationToken token)
    {
        var request = new BlobReadRequest(first.Begin.Blob, 1, 0, first.Part.Bytes.Length);
        var refused = await callers.Sdk.ReadBlobRangeAsync(request, token);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token),
            ErrorCode.RevisionConflict, dispatched: true);
    }
}
