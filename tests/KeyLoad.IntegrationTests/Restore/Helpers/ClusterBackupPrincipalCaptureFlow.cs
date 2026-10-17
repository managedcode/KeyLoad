using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureFlow
{
    internal static async Task<ClusterBackupPrincipalCaptureState> RunAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var identity = await ClusterBackupPrincipalCaptureSeed.CreateAsync(source, seed, token).ConfigureAwait(false);
        return await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            identity.Credential, async (sdk, official) =>
            {
                var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token).ConfigureAwait(false));
                var request = new ClusterBackupOwnerRequest(ClusterRestoreRf3Protocol.CurrentVersion, Guid.NewGuid(),
                    seed.Directory.ControlOwner, Guid.ParseExact(status.NodeId, ClusterRestoreRf3Protocol.IdentityFormat));
                var outbox = await ClusterBackupPrincipalCaptureCurrent.CaptureAsync(seed, token).ConfigureAwait(false);
                var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.CaptureClusterBackupOwnerAsync(
                    request, token).ConfigureAwait(false));
                var state = new ClusterBackupPrincipalCaptureState(identity.Principal, identity.Credential, request, original, []);
                Func<Task> current = () => ClusterBackupPrincipalCaptureCurrent.RequireAsync(seed, outbox, state, token);
                await ClusterBackupPrincipalCaptureReceipts.RequireIdentityAsync(original, request).ConfigureAwait(false);
                await current().ConfigureAwait(false);
                await ClusterBackupPrincipalCaptureSeed.ConfigureAsync(source, identity.Principal with
                {
                    PolicyEpoch = ClusterBackupPrincipalCaptureProtocol.DeniedEpoch,
                    ClusterAdministrator = false
                }, token).ConfigureAwait(false);
                await ClusterBackupPrincipalCaptureRefusals.RequireAsync(sdk, official, seed.Partition,
                    state, current, token).ConfigureAwait(false);
                await ClusterBackupPrincipalCaptureSeed.ConfigureAsync(source, identity.Principal with
                {
                    PolicyEpoch = ClusterBackupPrincipalCaptureProtocol.RepairedEpoch
                }, token).ConfigureAwait(false);
                await ClusterBackupPrincipalCaptureReceipts.ReplayAsync(sdk, official, seed.Partition,
                    state, current, token).ConfigureAwait(false);
                return state;
            }, token).ConfigureAwait(false);
    }

    internal static async Task<ClusterBackupPrincipalCaptureState> CopyAsync(TwoRf3MembershipWave source,
        ClusterBackupPrincipalCaptureState state, string ownedRoot, CancellationToken token)
        => state with
        {
            Archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, [state.Original],
                Path.Combine(ownedRoot, ClusterBackupPrincipalCaptureProtocol.ArchiveRoot), token).ConfigureAwait(false)
        };

    internal static async Task ReplayAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        ClusterBackupPrincipalCaptureState state, CancellationToken token)
    {
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            state.Credential, async (sdk, official) =>
            {
                var outbox = await ClusterBackupPrincipalCaptureCurrent.CaptureAsync(seed, token).ConfigureAwait(false);
                await ClusterBackupPrincipalCaptureReceipts.ReplayAsync(sdk, official, seed.Partition, state,
                    () => ClusterBackupPrincipalCaptureCurrent.RequireAsync(seed, outbox, state, token), token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
    }
}
