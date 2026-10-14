using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Pages
{
    internal const string RangeTool = "keyload_blobs_read_range";
    private const string UploadTool = "keyload_blobs_upload_info";
    private const string ListTool = "keyload_blobs_list";
    private static readonly (long Offset, int Count)[] Windows = [(0L, 4), (65534L, 6), (65536L, 17), (65553L, 0)];
    internal static async Task CurrentAsync(RequestCqrsRf3Callers admin,
        BlobPartialReadCancellationR45State state, CancellationToken token)
    {
        var p = state.Publication;
        await BlobPublicCancellationAssertions.CurrentAsync(admin, state.Previous, [83], token);
        await SqlRf3Protocol.EqualAsync(p.Completed.Value, await PublicReadAssertions.MetadataAsync(
            await admin.Sdk.GetBlobMetadataAsync(new(p.Begin.Blob), token)));
        await SqlRf3Protocol.EqualAsync(p.Completed.Value, (await PublicReadAssertions.RequiredMetadataAsync(
            await admin.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(p.Begin.Blob), token))).Value);
        await RangeAsync(admin, state, 0L, 65536, null, token);
        await RangeAsync(admin, state, 65536L, 17, null, token);
        var upload = new BlobUploadInfoRequest(p.Begin.Blob, p.Begin.UploadId);
        var expectedUpload = p.Written[1].Value with
        {
            Status = BlobUploadStatus.Complete
        };
        await SqlRf3Protocol.EqualAsync(expectedUpload,
            (await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.GetBlobUploadInfoAsync(upload, token))).Upload);
        await SqlRf3Protocol.EqualAsync(expectedUpload,
            (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
                await admin.Mcp.CallAsync(UploadTool, upload, token))).Value.Upload);
        var list = new BlobListRequest(p.Begin.Blob.Partition, p.Begin.Blob.Resource, 4, null);
        var expectedList = new BlobListPage([state.Previous, p.Completed.Value], null);
        await SqlRf3Protocol.EqualAsync(expectedList, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListBlobsAsync(list, token)));
        await SqlRf3Protocol.EqualAsync(expectedList, (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await admin.Mcp.CallAsync(ListTool, list, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await BlobLiveListR43Pages.OutboxAsync(admin, p.Begin.Blob.Partition, token));
        var placement = new AtomicPartitionPlacementReadRequest(1, p.Begin.Blob.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
    }
    internal static async Task HealthyAsync(BlobPublicCancellationOwner owner,
        BlobPartialReadCancellationR45State state, CancellationToken token)
    {
        Task Observe() => CurrentAsync(owner.Administrator, state, token);
        foreach (var window in Windows)
        {
            await RangeAsync(owner.Caller, state, window.Offset, window.Count, Observe, token);
        }
    }
    private static async Task RangeAsync(RequestCqrsRf3Callers caller,
        BlobPartialReadCancellationR45State state, long offset, int count,
        Func<Task>? observe, CancellationToken token)
    {
        var request = new BlobReadRequest(state.Publication.Begin.Blob, 1L, offset, count);
        var expected = new BlobReadResult(state.Publication.Completed.Value, offset,
            BlobPartialReadCancellationR45Corpus.Bytes(offset, count));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ReadBlobRangeAsync(request, token)));
        if (observe is not null)
        {
            await observe();
        }
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await caller.Mcp.CallAsync(RangeTool, request, token))).Value);
        if (observe is not null)
        {
            await observe();
        }
    }
}
