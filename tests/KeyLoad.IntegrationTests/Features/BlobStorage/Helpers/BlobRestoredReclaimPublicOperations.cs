using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicOperations
{
    internal static async Task PartialAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, Guid incarnation, CancellationToken token)
    {
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, false, token);
        await BlobRestoredReclaimPublicAssertions.QuotaAsync(sdk, official, state, state.Before, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Active.Begin.Blob.Partition), token));
        await Assert.That(placement.Incarnation).IsEqualTo(incarnation);
        state.FirstResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReclaimBlobAsync(state.First, token));
        await BlobRestoredReclaimPublicAssertions.ResultAsync(state.FirstResult, state.First, placement, false);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, false, token);
        await ReplayAsync(sdk, official, state, false, token);
        await BlobRestoredReclaimPublicAssertions.QuotaAsync(sdk, official, state, state.Over, token);
        state.Healthy = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official, state.Active.Begin.Blob, 1, true, 0, token);
        await Assert.That(state.Healthy.Completed!.Value.Length).IsEqualTo(65553L);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, false, token);
    }

    internal static async Task FinalAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, Guid incarnation, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Active.Begin.Blob.Partition), token));
        await Assert.That(placement.Incarnation).IsEqualTo(incarnation);
        state.FinalResult = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(
            await official.CallAsync("keyload_blobs_reclaim", state.Final, token))).Value;
        await BlobRestoredReclaimPublicAssertions.ResultAsync(state.FinalResult, state.Final, placement, true);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, true, token);
        await ReplayAsync(sdk, official, state, true, token);
    }

    private static async Task HealthyReplayAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, bool final, CancellationToken token)
    {
        var version = state.Healthy!;
        await SqlRf3Protocol.EqualAsync(version.Begun, await McpCallerAssertions.SdkSuccessAsync(await sdk.BeginBlobUploadAsync(version.Begin, token)));
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        await SqlRf3Protocol.EqualAsync(version.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await official.CallAsync("keyload_blobs_begin_upload", version.Begin, token))).Value);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        for (var index = 0; index < version.Parts.Length; index++)
        {
            await SqlRf3Protocol.EqualAsync(version.Written[index], await McpCallerAssertions.SdkSuccessAsync(await sdk.WriteBlobPartAsync(version.Parts[index], token)));
            await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
            await SqlRf3Protocol.EqualAsync(version.Written[index], (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await official.CallAsync("keyload_blobs_write_part", version.Parts[index], token))).Value);
            await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        }
        await SqlRf3Protocol.EqualAsync(version.Completed!, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteBlobUploadAsync(version.Complete!, token)));
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        await SqlRf3Protocol.EqualAsync(version.Completed!, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await official.CallAsync("keyload_blobs_complete_upload", version.Complete!, token))).Value);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, bool final, CancellationToken token)
    {
        var request = final ? state.Final : state.First;
        var expected = final ? state.FinalResult! : state.FirstResult!;
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReclaimBlobAsync(request, token)));
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(
            await official.CallAsync("keyload_blobs_reclaim", request, token))).Value);
        await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, final, token);
        if (state.Healthy is not null)
        { await HealthyReplayAsync(sdk, official, state, final, token); }
        if (final)
        {
            await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ReclaimBlobAsync(state.First, token), ErrorCode.TokenInvalidated);
            await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, true, token);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync("keyload_blobs_reclaim", state.First, token), ErrorCode.TokenInvalidated, dispatched: true);
            await BlobRestoredReclaimPublicAssertions.CurrentAsync(sdk, official, state, true, token);
        }
    }
}
