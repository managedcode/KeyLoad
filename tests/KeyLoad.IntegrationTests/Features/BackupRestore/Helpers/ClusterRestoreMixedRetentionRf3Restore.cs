using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Restore
{
    internal static Task PausedAsync(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.Key,
            async callers =>
            {
                state.CurrentSourceOwner = target.Mappings.Single(mapping => mapping.Source.PhysicalShardId == state.SourceOwner.PhysicalShardId).Target;
                state.CurrentTargetOwner = target.Mappings.Single(mapping => mapping.Source.PhysicalShardId == state.TargetOwner.PhysicalShardId).Target;
                await ClusterRestoreMixedRetentionRf3Literal.OriginalAsync(callers, state, token);
                foreach (var original in state.OriginalQueue.Commands)
                {
                    await RefusedAsync(callers, state, McpCallerTools.DocumentsCommit, original.Command,
                        () => callers.Sdk.CommitAsync(original.Command, token), ErrorCode.TokenInvalidated, token);
                }
                await RefusedAsync(callers, state, TargetInboxRf3Protocol.Tool, state.Inbox,
                    () => callers.Sdk.CommitInboxAsync(state.Inbox, token), ErrorCode.TokenInvalidated, token);
                var queue = new DeliveryCommand(Guid.NewGuid(), state.Source, state.InputReceived.Deliveries.Single().Token, DeliveryAction.Ack);
                await RefusedAsync(callers, state, McpCallerTools.MessagesComplete, queue,
                    () => callers.Sdk.CompleteAsync(queue, token), ErrorCode.TokenInvalidated, token);
                var old = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.FilteredGroup,
                    state.FilteredReceived.Deliveries.First().Token, DeliveryAction.Ack);
                await RefusedAsync(callers, state, McpCallerTools.SubscriptionsComplete, old,
                    () => callers.Sdk.CompleteSubscriptionAsync(old, token), ErrorCode.TokenInvalidated, token);
                var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.FilteredGroup);
                await RefusedAsync(callers, state, McpCallerTools.SubscriptionsReceive, receive,
                    () => callers.Sdk.ReceiveSubscriptionAsync(receive, token), ErrorCode.DispatchPaused, token);
                await ClusterRestoreMixedRetentionRf3Literal.OriginalAsync(callers, state, token);
                return true;
            }, token);

    internal static Task ContinueAsync(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state,
        string rootKey, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node1, rootKey,
            async root =>
            {
                await ClusterRestoreMixedRetentionRf3Privacy.RunAsync(target, root.Sdk, state, token);
                await RequestCqrsRf3Callers.RunOwnedAsync(target.Application, TwoRf3MembershipProtocol.Node4, state.Key,
                    async callers =>
                    {
                        await ClusterRestoreMixedRetentionRf3Queue.ContinueAsync(callers, state, token);
                        await ClusterRestoreMixedRetentionRf3FilteredTopic.ContinueAsync(callers, state, token);
                        await ClusterRestoreMixedRetentionRf3Inbox.ContinueAsync(callers, state, token);
                        state.Continued = true;
                        await ClusterRestoreMixedRetentionRf3Literal.TopicsAsync(callers, state, token);
                        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state, token);
                        await ClusterRestoreMixedRetentionRf3Cold.ReplayAsync(callers, state, token);
                        return true;
                    }, token);
                return true;
            }, token);

    internal static async Task RefusedAsync<T>(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state,
        string tool, object request, Func<Task<Result<T>>> sdk, ErrorCode expected, CancellationToken token)
    {
        await TargetInboxRf3Assertions.DeniedAsync(await sdk(), expected);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(tool, request, token), expected, dispatched: true);
        var partition = request switch
        {
            DeliveryCommand delivery => delivery.Lane.Partition,
            ReceiveRequest receive => receive.Lane.Partition,
            _ => state.Partition
        };
        var id = request switch
        {
            CommitInboxRequest inbox => (Guid?)inbox.CommandId,
            _ => ClusterRestoreRf3EventingReadOracle.CommandId(request)
        };
        var sql = SqlRf3Protocol.Call(partition, tool, request, id);
        await TargetInboxRf3Assertions.DeniedAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), expected, dispatched: true);
    }
}
