using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActivePolicyPublicAssertions
{
    internal static async Task CurrentAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.UploadAsync(administrator, state.Blob,
            state.Begin.UploadId, state.Current, token);
        var metadata = new BlobMetadataRequest(state.Blob);
        await SqlRf3Protocol.EqualAsync(state.Completed?.Value,
            await PublicReadAssertions.MetadataAsync(await creator.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlRf3Protocol.EqualAsync(state.Completed?.Value,
            (await PublicReadAssertions.MetadataAsync(await creator.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
        var list = new BlobListRequest(state.Blob.Partition, state.Blob.Resource, 4);
        var expected = new BlobListPage(state.Completed is { } done ? [done.Value] : [], null);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ListBlobsAsync(list, token)));
        await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<BlobListPage>(await creator.Mcp.CallAsync("keyload_blobs_list", list, token))).Value);
        if (state.Completed is { } published)
        {
            foreach (var read in new[] { new BlobReadRequest(state.Blob, 1L, 0L, 4), new BlobReadRequest(state.Blob, 1L, 4L, 0) })
            {
                var bytes = read.Count == 0 ? Array.Empty<byte>() : state.Bytes;
                var result = new BlobReadResult(published.Value, read.Offset, bytes);
                await SqlRf3Protocol.EqualAsync(result, await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReadBlobRangeAsync(read, token)));
                await SqlRf3Protocol.EqualAsync(result,
                    (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await creator.Mcp.CallAsync("keyload_blobs_read_range", read, token))).Value);
            }
        }
    }

    internal static async Task ReceiptAsync(BlobActivePolicyPublicState state, Guid id, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }

    internal static async Task RefusedAsync<T>(RequestCqrsRf3Callers creator, string tool, object request,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> call, Func<Task> observe, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await call(token), ErrorCode.PermissionDenied);
        await observe();
        await McpCallerAssertions.ErrorAsync(await creator.Mcp.CallAsync(tool, request, token), ErrorCode.PermissionDenied, dispatched: true);
        await observe();
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, bool begin, CancellationToken token)
    {
        Task Observe() => CurrentAsync(administrator, creator, state, token);
        if (begin)
        {
            await RefusedAsync(creator, "keyload_blobs_begin_upload", state.Begin, ct => creator.Sdk.BeginBlobUploadAsync(state.Begin, ct), Observe, token);
        }
        await RefusedAsync(creator, "keyload_blobs_write_part", state.DeniedPart, ct => creator.Sdk.WriteBlobPartAsync(state.DeniedPart, ct), Observe, token);
        await RefusedAsync(creator, "keyload_blobs_complete_upload", state.DeniedComplete, ct => creator.Sdk.CompleteBlobUploadAsync(state.DeniedComplete, ct), Observe, token);
        await RefusedAsync(creator, "keyload_blobs_abort_upload", state.DeniedAbort, ct => creator.Sdk.AbortBlobUploadAsync(state.DeniedAbort, ct), Observe, token);
    }
}
