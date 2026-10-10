using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Privacy
{
    internal static async Task RunAsync(ClusterRestoreRf3Fixture target, KeyLoadClient root,
        ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.InspectorKey,
            async caller =>
            {
                await InspectorOwnedAsync(caller, state, token);
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
                    state.Inspector with { Revoked = true, PolicyEpoch = ClusterRestoreMixedRetentionRf3Protocol.RevokedEpoch }, token));
                var inspect = new InspectMessageRequest(state.OriginalQueue.Lane, QueueLifecyclePublicProtocol.Pending);
                await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(caller, state, McpCallerTools.MessagesInspect,
                    inspect, () => caller.Sdk.InspectAsync(inspect, token), ErrorCode.Unauthenticated, token);
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
                    state.Inspector with { PolicyEpoch = ClusterRestoreMixedRetentionRf3Protocol.RepairedEpoch }, token));
                await InspectorOwnedAsync(caller, state, token);
                return true;
            }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.Key,
            async caller =>
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
                    state.Principal with { Revoked = true, PolicyEpoch = ClusterRestoreMixedRetentionRf3Protocol.RevokedEpoch }, token));
                var denied = state.Inbox with { CommandId = Guid.NewGuid() };
                await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(caller, state, TargetInboxRf3Protocol.Tool, denied,
                    () => caller.Sdk.CommitInboxAsync(denied, token), ErrorCode.Unauthenticated, token);
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
                    state.Principal with { PolicyEpoch = ClusterRestoreMixedRetentionRf3Protocol.RepairedEpoch }, token));
                await OldCommandsAsync(caller, state, token);
                await ClusterRestoreMixedRetentionRf3Literal.OriginalAsync(caller, state, token);
                return true;
            }, token);
    }

    internal static Task InspectorAsync(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.InspectorKey,
            async caller => { await InspectorOwnedAsync(caller, state, token); return true; }, token);

    private static async Task InspectorOwnedAsync(RequestCqrsRf3Callers caller, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        foreach (var id in new[] { QueueLifecyclePublicProtocol.Parked, QueueLifecyclePublicProtocol.Pending })
        {
            var inspect = new InspectMessageRequest(state.OriginalQueue.Lane, id);
            await ClusterRestoreMixedRetentionRf3Literal.FourAsync<MessageInspection?>(caller, state.Partition,
                McpCallerTools.MessagesInspect, inspect,
                new(Metadata(state, id), state.Continued ? null : "{}", state.Continued ? null : "{}"),
                () => caller.Sdk.InspectAsync(inspect, token), token);
        }
    }

    private static MessageMetadata Metadata(ClusterRestoreMixedRetentionRf3State state, string id)
        => id == QueueLifecyclePublicProtocol.Parked
            ? state.Continued
                ? new(id, MessageState.Cancelled, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Four,
                    QueueLifecyclePublicProtocol.One, null, null, LeaseVersion: QueueLifecyclePublicProtocol.Two,
                    DeliveryGeneration: QueueLifecyclePublicProtocol.Two, SafeFailureCode: "AttemptsExhausted")
                : new(id, MessageState.DeadLettered, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Three,
                    QueueLifecyclePublicProtocol.One, null, null, LeaseVersion: QueueLifecyclePublicProtocol.One,
                    SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueLifecyclePublicProtocol.One)
            : state.Continued
                ? new(id, MessageState.Acked, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Seven,
                    QueueLifecyclePublicProtocol.Three, null, null, LeaseVersion: QueueLifecyclePublicProtocol.Three,
                    DeliveryGeneration: QueueLifecyclePublicProtocol.Two)
                : new(id, MessageState.PendingDeadLetter, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Three,
                    QueueLifecyclePublicProtocol.Two, null, null, LeaseVersion: QueueLifecyclePublicProtocol.One,
                    SafeFailureCode: "AttemptsExhausted");

    internal static async Task OldCommandsAsync(RequestCqrsRf3Callers caller, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        foreach (var original in state.OriginalQueue.Commands)
        {
            await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(caller, state, McpCallerTools.DocumentsCommit,
                original.Command, () => caller.Sdk.CommitAsync(original.Command, token), ErrorCode.PermissionDenied, token);
        }
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(caller, state, TargetInboxRf3Protocol.Tool,
            state.Inbox, () => caller.Sdk.CommitInboxAsync(state.Inbox, token), ErrorCode.PermissionDenied, token);
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(caller, state, McpCallerTools.DocumentsCommit,
            state.Purge, () => caller.Sdk.CommitAsync(state.Purge, token), ErrorCode.PermissionDenied, token);
    }
}
