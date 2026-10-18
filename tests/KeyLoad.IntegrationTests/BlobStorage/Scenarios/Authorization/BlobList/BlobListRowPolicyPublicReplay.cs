using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobListRowPolicyPublicReplay
{
    internal static async Task RunAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        foreach (var version in versions)
        {
            await SqlClusterProtocol.EqualAsync(version.Begun, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BeginBlobUploadAsync(version.Begin, token)));
            await CurrentAsync(admin, reader, versions, token);
            await SqlClusterProtocol.EqualAsync(version.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await admin.Mcp.CallAsync("keyload_blobs_begin_upload", version.Begin, token))).Value);
            await CurrentAsync(admin, reader, versions, token);
            await SqlClusterProtocol.EqualAsync(version.Written, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.WriteBlobPartAsync(version.Part, token)));
            await CurrentAsync(admin, reader, versions, token);
            await SqlClusterProtocol.EqualAsync(version.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await admin.Mcp.CallAsync("keyload_blobs_write_part", version.Part, token))).Value);
            await CurrentAsync(admin, reader, versions, token);
            await SqlClusterProtocol.EqualAsync(version.Completed, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CompleteBlobUploadAsync(version.Complete, token)));
            await CurrentAsync(admin, reader, versions, token);
            await SqlClusterProtocol.EqualAsync(version.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
                await admin.Mcp.CallAsync("keyload_blobs_complete_upload", version.Complete, token))).Value);
            await CurrentAsync(admin, reader, versions, token);
        }
    }

    private static async Task CurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        await BlobListRowPolicyPublicAssertions.OwnerAAsync(reader, versions, token);
        await BlobListRowPolicyPublicAssertions.AllAsync(admin, versions, token);
    }
}
