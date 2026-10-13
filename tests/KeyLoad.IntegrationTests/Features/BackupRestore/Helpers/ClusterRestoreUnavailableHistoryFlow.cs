using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreUnavailableHistoryFlow
{
    private const int ReplicaCount = 3;
    private const int FirstOwnerIndex = 0;
    internal static Task<bool> RunAsync(ClusterRestoreRf3Fixture target,
        ClusterRestoreMixedRetentionRf3State state, string rootKey, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(target.Application, CaptureNode(target, state), rootKey,
            async (sdk, official) =>
            {
                var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token).ConfigureAwait(false));
                var beforeRequest = new ClusterBackupOwnerRequest(ClusterRestoreRf3Protocol.CurrentVersion,
                    Guid.NewGuid(), state.CurrentTargetOwner,
                    Guid.ParseExact(status.NodeId, ClusterRestoreRf3Protocol.IdentityFormat));
                var before = await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.BackupClient.CaptureClusterBackupOwnerAsync(
                    sdk, beforeRequest, token).ConfigureAwait(false));
                await ClusterRestoreUnavailableHistoryAssertions.IdentityAsync(before, beforeRequest);
                _ = await RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4,
                    state.Key, async callers =>
                    {
                        await RefusalsAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreUnavailableHistoryAssertions.PageAsync(callers, state, token).ConfigureAwait(false);
                        await RefusalsAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3Literal.TopicsAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token).ConfigureAwait(false);
                        return true;
                    }, token).ConfigureAwait(false);
                var afterRequest = beforeRequest with { CaptureId = Guid.NewGuid() };
                var after = await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.BackupClient.CaptureClusterBackupOwnerAsync(
                    sdk, afterRequest, token).ConfigureAwait(false));
                await ClusterRestoreUnavailableHistoryAssertions.IdentityAsync(after, afterRequest);
                await SqlRf3Protocol.EqualAsync(before.Cut with { CaptureId = afterRequest.CaptureId }, after.Cut);
                return true;
            }, token);

    private static string CaptureNode(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state)
    {
        var ownerIndex = target.Mappings.ToList().FindIndex(mapping =>
            mapping.Source.PhysicalShardId == state.TargetOwner.PhysicalShardId);
        if (ownerIndex < FirstOwnerIndex)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        return ClusterRestoreRf3Protocol.Nodes.Skip(ownerIndex * ReplicaCount).First();
    }

    private static async Task RefusalsAsync(RequestCqrsRf3Callers callers,
        ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var beginning = new ReadEventSourceRequest(state.PurgedSource);
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(callers, state, McpCallerTools.EventsRead,
            beginning, () => callers.Sdk.ReadEventSourceAsync(beginning, token), ErrorCode.HistoryUnavailable, token);
        var missing = new ReadEventSourceRequest(state.PurgedSource,
            AfterPosition: ClusterRestoreMixedRetentionRf3Protocol.FirstRevision);
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(callers, state, McpCallerTools.EventsRead,
            missing, () => callers.Sdk.ReadEventSourceAsync(missing, token), ErrorCode.HistoryUnavailable, token);
    }
}
