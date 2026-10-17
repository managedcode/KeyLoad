using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobGlobalQuotaR49Current
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, CancellationToken token)
    {
        var placement = new AtomicPartitionPlacementReadRequest(1, state.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
        foreach (var upload in state.CurrentUploads.Where(upload => state.ConfiguredResources.Contains(upload.Blob.Resource)))
        {
            await UploadAsync(callers, state, upload, token);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(state.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
    }
    private static async Task UploadAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, CancellationToken token)
    {
        var selected = upload.AcceptedBegin ?? upload.Begin;
        var info = new BlobUploadInfoRequest(upload.Blob, selected.UploadId);
        await SqlRf3Protocol.EqualAsync(upload.Progress, await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(info, token)));
        await SqlRf3Protocol.EqualAsync(upload.Progress, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(await callers.Mcp.CallAsync("keyload_blobs_upload_info", info, token))).Value.Upload);
        var metadata = ReferenceEquals(upload, state.Attempt) && state.Healthy is { } healthy ? healthy.Metadata : upload.Metadata;
        await SqlRf3Protocol.EqualAsync(metadata, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(new(upload.Blob), token)));
        await SqlRf3Protocol.EqualAsync(metadata, (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(await callers.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(upload.Blob), token))).Value.Metadata);
        if (upload.Metadata is { } published)
        {
            await RangeAsync(callers, upload.Blob, published, 0, BlobGlobalQuotaR49Corpus.PartLength, token);
            await RangeAsync(callers, upload.Blob, published, BlobGlobalQuotaR49Corpus.PartLength, 4, token);
        }
    }
    internal static async Task WindowsAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, CancellationToken token)
    {
        foreach (var upload in state.CurrentUploads.Where(upload => upload.Metadata is not null))
        {
            int[] offsets = [0, 65_534, 65_536, BlobGlobalQuotaR49Corpus.SmallLength];
            int[] counts = [4, 6, 4, 0];
            for (var index = 0; index < offsets.Length; index++)
            {
                var request = new BlobReadRequest(upload.Blob, 1L, offsets[index], counts[index]);
                var expected = new BlobReadResult(upload.Metadata!, offsets[index], BlobGlobalQuotaR49Corpus.Bytes(offsets[index], counts[index]));
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
                await RunAsync(callers, state, token);
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
                await RunAsync(callers, state, token);
            }
        }
    }
    private static async Task RangeAsync(RequestCqrsRf3Callers callers, BlobRef blob, BlobMetadata metadata, int offset, int count, CancellationToken token)
    {
        var expected = new BlobReadResult(metadata, offset, BlobGlobalQuotaR49Corpus.Bytes(offset, count));
        var request = new BlobReadRequest(blob, 1L, offset, count);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }
}
