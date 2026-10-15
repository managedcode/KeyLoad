using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureCurrent
{
    internal static async Task<OutboxStatus> CaptureAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await seed.Source.OutboxStatusAsync(seed.Partition,
            token).ConfigureAwait(false));

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        OutboxStatus original, ClusterBackupPrincipalCaptureState state, CancellationToken token)
    {
        await seed.VerifyAsync(token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await CaptureAsync(seed, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await seed.Official.CallAsync(ClusterRestoreRf3EventingProtocol.OutboxStatus,
                new GetOutboxStatusRequest(seed.Partition), token).ConfigureAwait(false))).Value);
        if (!state.Archives.IsDefaultOrEmpty)
        {
            await ClusterRestoreRf3NativeArchive.RequireOriginalAsync([state.Original], state.Archives,
                token).ConfigureAwait(false);
        }
    }
}
