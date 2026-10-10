using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Cold
{
    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.Key,
            async callers =>
            {
                await ClusterRestoreMixedRetentionRf3Privacy.OldCommandsAsync(callers, state, token);
                await ClusterRestoreMixedRetentionRf3Queue.HealthyAsync(callers, state, token);
                await ClusterRestoreMixedRetentionRf3Literal.TopicsAsync(callers, state, token);
                await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token);
                await ReplayAsync(callers, state, token);
                return true;
            }, token);
        await ClusterRestoreMixedRetentionRf3Privacy.InspectorAsync(target, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await ClusterRestoreMixedRetentionRf3Queue.ReplayAsync(callers, state, token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, TargetInboxRf3Protocol.Tool,
            state.FreshInbox, state.FreshInboxResult, () => callers.Sdk.CommitInboxAsync(state.FreshInbox, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Source.Partition, McpCallerTools.MessagesComplete,
            state.FreshAck, state.FreshAckReceipt, () => callers.Sdk.CompleteAsync(state.FreshAck, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Source.Partition, McpCallerTools.DocumentsCommit,
            state.SourceCancel, state.SourceCancelReceipt, () => callers.Sdk.CommitAsync(state.SourceCancel, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Source.Partition, McpCallerTools.DocumentsCommit,
            state.FreshEnqueue, state.FreshEnqueueReceipt, () => callers.Sdk.CommitAsync(state.FreshEnqueue, token), token);
        foreach (var (command, receipt) in state.CurrentSubscriptionAcks)
        {
            await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, McpCallerTools.SubscriptionsComplete,
                command, receipt, () => callers.Sdk.CompleteSubscriptionAsync(command, token), token);
        }
        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token);
    }
}
