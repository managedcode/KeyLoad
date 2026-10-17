using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Pages
{
    private const string MetadataTool = "keyload_blobs_metadata";
    private const string UploadTool = "keyload_blobs_upload_info";
    private const string ListTool = "keyload_blobs_list";
    internal const string RangeTool = "keyload_blobs_read_range";
    internal static readonly (long Offset, int Count)[] Ranges = [(0L, 4), (65534L, 6), (65536L, 17), (65553L, 0)];
    internal static async Task CurrentAsync(RequestCqrsRf3Callers admin, BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        await MetadataAsync(admin, state, null, token);
        foreach (var range in new (long Offset, int Count)[] { (0L, 65536), (65536L, 17) })
        {
            await RangeAsync(admin, state, range.Offset, range.Count, null, token);
        }
        var upload = new BlobUploadInfoRequest(state.Published.Begin.Blob, state.Published.Begin.UploadId);
        var expected = state.Published.Written[1].Value with
        {
            Status = BlobUploadStatus.Complete
        };
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.GetBlobUploadInfoAsync(upload, token))).Upload);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(await admin.Mcp.CallAsync(UploadTool, upload, token))).Value.Upload);
        var blob = state.Published.Begin.Blob;
        var list = new BlobListRequest(blob.Partition, blob.Resource, 2, null);
        var page = new BlobListPage([state.Published.Completed.Value], null);
        await SqlRf3Protocol.EqualAsync(page, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListBlobsAsync(list, token)));
        await SqlRf3Protocol.EqualAsync(page, (await McpCallerAssertions.SuccessAsync<BlobListPage>(await admin.Mcp.CallAsync(ListTool, list, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await BlobLiveListR43Pages.OutboxAsync(admin, blob.Partition, token));
        var placement = new AtomicPartitionPlacementReadRequest(1, blob.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
    }
    internal static async Task HealthyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        Task Observe() => CurrentAsync(admin, state, token);
        await MetadataAsync(reader, state, Observe, token);
        foreach (var range in Ranges)
        {
            await RangeAsync(reader, state, range.Offset, range.Count, Observe, token);
        }
    }
    private static async Task MetadataAsync(RequestCqrsRf3Callers caller, BlobPrincipalRevokedR44State state,
        Func<Task>? observe, CancellationToken token)
    {
        var request = new BlobMetadataRequest(state.Published.Begin.Blob);
        await SqlRf3Protocol.EqualAsync(state.Published.Completed.Value, await PublicReadAssertions.MetadataAsync(await caller.Sdk.GetBlobMetadataAsync(request, token)));
        if (observe is not null)
        {
            await observe();
        }
        await SqlRf3Protocol.EqualAsync(state.Published.Completed.Value, (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(await caller.Mcp.CallAsync(MetadataTool, request, token))).Value.Metadata);
        if (observe is not null)
        {
            await observe();
        }
    }
    private static async Task RangeAsync(RequestCqrsRf3Callers caller, BlobPrincipalRevokedR44State state,
        long offset, int count, Func<Task>? observe, CancellationToken token)
    {
        var request = new BlobReadRequest(state.Published.Begin.Blob, 1L, offset, count);
        var expected = new BlobReadResult(state.Published.Completed.Value, offset, BlobPrincipalRevokedR44Corpus.Bytes(offset, count));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadBlobRangeAsync(request, token)));
        if (observe is not null)
        {
            await observe();
        }
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await caller.Mcp.CallAsync(RangeTool, request, token))).Value);
        if (observe is not null)
        {
            await observe();
        }
    }
}
