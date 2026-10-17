using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartIdentityR40Assertions
{
    private const string MetadataTool = "keyload_blobs_metadata";
    private const string UploadTool = "keyload_blobs_upload_info";
    private const string ListTool = "keyload_blobs_list";
    private const string RangeTool = "keyload_blobs_read_range";
    private const string Missing = "The published blob is unavailable.";
    private const string SafeMissing = "The database operation could not be completed.";
    private const string DetailProperty = "detail";
    private const long DeclaredLength = 65540L;

    internal static async Task CurrentAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        await CanonicalAsync(callers, state, token);
        if (state.Completed is null)
        {
            await HiddenRangeAsync(callers, state, token);
        }
        else
        {
            await PublishedRangesAsync(callers, state, token);
        }
        await OutboxAsync(callers, state, token);
    }

    private static async Task CanonicalAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        var upload = new BlobUploadInfoRequest(state.Begin.Blob, state.Begin.UploadId);
        await SqlRf3Protocol.EqualAsync(state.Current, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(upload, token)));
        await SqlRf3Protocol.EqualAsync(state.Current, (await PublicReadAssertions.RequiredUploadAsync(
            await callers.Mcp.CallAsync(UploadTool, upload, token))).Value);
        var metadata = new BlobMetadataRequest(state.Begin.Blob);
        await SqlRf3Protocol.EqualAsync(state.Completed?.Value, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlRf3Protocol.EqualAsync(new BlobMetadataReadResult(state.Completed?.Value),
            (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(await callers.Mcp.CallAsync(MetadataTool, metadata, token))).Value);
        var list = new BlobListRequest(state.Begin.Blob.Partition, state.Begin.Blob.Resource, 4);
        var expected = state.Completed is null ? new BlobListPage([], null) : new BlobListPage([state.Completed.Value], null);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ListBlobsAsync(list, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobListPage>(await callers.Mcp.CallAsync(ListTool, list, token))).Value);
        await OutboxAsync(callers, state, token);
    }

    private static async Task HiddenRangeAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        foreach (var (offset, count) in new[] { (0L, 1), (0L, 0), (DeclaredLength, 0) })
        {
            var request = new BlobReadRequest(state.Begin.Blob, 1L, offset, count);
            var result = await callers.Sdk.ReadBlobRangeAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.NotFound, Missing), result.Problem!);
            await CanonicalAsync(callers, state, token);
            var reply = await callers.Mcp.CallAsync(RangeTool, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.NotFound, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(DetailProperty).GetString()).IsEqualTo(SafeMissing);
            await CanonicalAsync(callers, state, token);
        }
    }

    private static async Task PublishedRangesAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        foreach (var (offset, count) in new[] { (0, 65536), (65536, 4), (65534, 6), (65540, 0) })
        {
            var request = new BlobReadRequest(state.Begin.Blob, 1L, offset, count);
            var expected = new BlobReadResult(state.Completed!.Value, offset, BlobPartIdentityR40Corpus.Bytes(offset, count));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync(RangeTool, request, token))).Value);
        }
    }

    internal static async Task OutboxAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(state.Begin.Blob.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Begin.Blob.Partition), token))).Value);
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, BlobPartIdentityR40State state)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Begin.Blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
