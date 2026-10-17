using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSelectiveReadR43Replay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers admin, BlobSelectiveReadR43State state,
        CancellationToken token)
    {
        foreach (var version in state.Versions)
        {
            await SqlRf3Protocol.EqualAsync(version.Begun, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.BeginBlobUploadAsync(version.Begin, token)));
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
            await SqlRf3Protocol.EqualAsync(version.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await admin.Mcp.CallAsync("keyload_blobs_begin_upload", version.Begin, token))).Value);
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
            await SqlRf3Protocol.EqualAsync(version.Written, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.WriteBlobPartAsync(version.Part, token)));
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
            await SqlRf3Protocol.EqualAsync(version.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await admin.Mcp.CallAsync("keyload_blobs_write_part", version.Part, token))).Value);
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
            await SqlRf3Protocol.EqualAsync(version.Completed, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CompleteBlobUploadAsync(version.Complete, token)));
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
            await SqlRf3Protocol.EqualAsync(version.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
                await admin.Mcp.CallAsync("keyload_blobs_complete_upload", version.Complete, token))).Value);
            await BlobSelectiveReadR43Reads.CurrentAsync(admin, state, token);
        }
    }
}
