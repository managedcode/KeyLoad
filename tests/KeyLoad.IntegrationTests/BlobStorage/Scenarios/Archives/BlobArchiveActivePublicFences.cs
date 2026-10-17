using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicFences
{
    internal static async Task RequireAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobArchiveActivePublicState state, CancellationToken token)
    {
        foreach (var version in new[] { state.OriginalPublished, state.Active })
        {
            await RejectAsync(async () => await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.BeginBlobUploadAsync(version.Begin, token), ErrorCode.TokenInvalidated), official, "keyload_blobs_begin_upload", version.Begin, state, sdk, token);
            foreach (var part in version.Parts)
            {
                await RejectAsync(async () => await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.WriteBlobPartAsync(part, token), ErrorCode.TokenInvalidated), official, "keyload_blobs_write_part", part, state, sdk, token);
            }
            if (version.Complete is { } complete)
            {
                await RejectAsync(async () => await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.CompleteBlobUploadAsync(complete, token), ErrorCode.TokenInvalidated), official, "keyload_blobs_complete_upload", complete, state, sdk, token);
            }
        }
        await RejectAsync(async () => await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.WriteBlobPartAsync(state.RefusedPart, token), ErrorCode.TokenInvalidated), official, "keyload_blobs_write_part", state.RefusedPart, state, sdk, token);
    }

    private static async Task RejectAsync(Func<Task> operation,
        McpOfficialClient official, string tool, object request, BlobArchiveActivePublicState state,
        KeyLoad.Client.KeyLoadClient sdk, CancellationToken token)
    {
        await operation();
        await HealthyAsync(sdk, official, state, token);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(tool, request, token), ErrorCode.TokenInvalidated, dispatched: true);
        await HealthyAsync(sdk, official, state, token);
    }

    internal static async Task HealthyAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobArchiveActivePublicState state, CancellationToken token)
    {
        await BlobArchiveActivePublicAssertions.CurrentAsync(sdk, official, state.Published, token);
        await BlobArchiveActivePublicAssertions.ActiveAsync(sdk, official, state.Active, true, token);
    }

    internal static async Task ReplayAsync(KeyLoad.Client.KeyLoadClient sdk, McpOfficialClient official,
        BlobArchiveActivePublicState state, CancellationToken token)
    {
        var version = state.Published;
        await SqlRf3Protocol.EqualAsync(version.Begun, await McpCallerAssertions.SdkSuccessAsync(await sdk.BeginBlobUploadAsync(version.Begin, token)));
        await HealthyAsync(sdk, official, state, token);
        await SqlRf3Protocol.EqualAsync(version.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await official.CallAsync("keyload_blobs_begin_upload", version.Begin, token))).Value);
        await HealthyAsync(sdk, official, state, token);
        for (var index = 0; index < version.Parts.Length; index++)
        {
            await SqlRf3Protocol.EqualAsync(version.Written[index], await McpCallerAssertions.SdkSuccessAsync(await sdk.WriteBlobPartAsync(version.Parts[index], token)));
            await HealthyAsync(sdk, official, state, token);
            await SqlRf3Protocol.EqualAsync(version.Written[index], (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await official.CallAsync("keyload_blobs_write_part", version.Parts[index], token))).Value);
            await HealthyAsync(sdk, official, state, token);
        }
        await SqlRf3Protocol.EqualAsync(version.Completed!, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteBlobUploadAsync(version.Complete!, token)));
        await HealthyAsync(sdk, official, state, token);
        await SqlRf3Protocol.EqualAsync(version.Completed!, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await official.CallAsync("keyload_blobs_complete_upload", version.Complete!, token))).Value);
        await HealthyAsync(sdk, official, state, token);
    }
}
