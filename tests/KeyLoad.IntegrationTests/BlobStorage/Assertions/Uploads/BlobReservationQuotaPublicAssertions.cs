using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReservationQuotaPublicAssertions
{
    private const string ErrorDetailProperty = "detail";

    internal static async Task RefusedAsync(RequestCqrsClusterCallers callers, BeginBlobUploadRequest request,
        CancellationToken token)
    {
        var sdk = await callers.Sdk.BeginBlobUploadAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        await Assert.That(sdk.Problem?.Detail).IsEqualTo("The blob reservation quota is exhausted.");
        var official = await callers.Mcp.CallAsync("keyload_blobs_begin_upload", request, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(ErrorDetailProperty).GetString()).IsEqualTo("The operation exceeds the available resource capacity.");
    }

    internal static async Task ReservedAsync(RequestCqrsClusterCallers callers,
        BlobReservationQuotaPublicPublication.Reservation state, BlobUploadInfo expected, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(state.First.Blob, state.First.UploadId);
        var actual = await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value;
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, official.Upload);
        var absent = new BlobUploadInfoRequest(state.Refused.Blob, state.Refused.UploadId);
        await Assert.That(await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(absent, token))).IsNull();
        await Assert.That((await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", absent, token))).Value.Upload).IsNull();
        await Assert.That(await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(new(state.Refused.Blob), token))).IsNull();
        await Assert.That((await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(await callers.Mcp.CallAsync(
            "keyload_blobs_metadata", new BlobMetadataRequest(state.Refused.Blob), token))).Value.Metadata).IsNull();
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers callers,
        BlobSupersessionPublicTrial.Version published, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(published.Completed.Value,
            await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(new(published.Begin.Blob), token)));
        await SqlClusterProtocol.EqualAsync(published.Completed.Value,
            (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(await callers.Mcp.CallAsync(
                "keyload_blobs_metadata", new BlobMetadataRequest(published.Begin.Blob), token))).Value.Metadata);
        var request = new BlobReadRequest(published.Begin.Blob, 1L, 0L, 1);
        var expected = new BlobReadResult(published.Completed.Value, 0L, new byte[] { 71 });
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }

    internal static async Task OriginalAsync(RequestCqrsClusterCallers callers,
        BlobReservationQuotaPublicPublication.Reservation state, AbortBlobUploadRequest abort,
        BlobCommitResult<BlobUploadInfo> aborted, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Begun, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.BeginBlobUploadAsync(state.First, token)));
        await AbortedAsync(callers, state.First, aborted.Value, token);
        await SqlClusterProtocol.EqualAsync(state.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.First, token))).Value);
        await AbortedAsync(callers, state.First, aborted.Value, token);
        await SqlClusterProtocol.EqualAsync(aborted, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(abort, token)));
        await SqlClusterProtocol.EqualAsync(aborted, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_abort_upload", abort, token))).Value);
        await Assert.That(state.Begun.Receipt.CommandId).IsEqualTo(state.First.CommandId);
        await Assert.That(aborted.Receipt.CommandId).IsEqualTo(abort.CommandId);
        await Assert.That(state.Begun.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(aborted.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    private static async Task AbortedAsync(RequestCqrsClusterCallers callers, BeginBlobUploadRequest first,
        BlobUploadInfo expected, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(first.Blob, first.UploadId);
        await SqlClusterProtocol.EqualAsync(expected,
            await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", request, token))).Value.Upload);
    }
}
