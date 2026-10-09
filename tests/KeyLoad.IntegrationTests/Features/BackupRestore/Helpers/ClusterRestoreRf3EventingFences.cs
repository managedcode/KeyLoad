using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingFences
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, CancellationToken cancellationToken)
    {
        var cursor = new ReadEventSourceRequest(state.Source, Cursor: state.Page.Cursor);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ReadEventSourceAsync(cursor,
            cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
        await OtherRoutesAsync(sdk, official, state.Partition, McpCallerTools.EventsRead, cursor,
            ErrorCode.TokenInvalidated, cancellationToken).ConfigureAwait(false);
        var queue = new DeliveryCommand(Guid.NewGuid(), state.Lane,
            state.QueueClaim.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Token, DeliveryAction.Ack);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.CompleteAsync(queue,
            cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
        await OtherRoutesAsync(sdk, official, state.Partition, McpCallerTools.MessagesComplete, queue,
            ErrorCode.TokenInvalidated, cancellationToken).ConfigureAwait(false);
        var delivery = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group,
            state.Received.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Token, DeliveryAction.Ack);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.CompleteSubscriptionAsync(delivery,
            cancellationToken).ConfigureAwait(false), ErrorCode.TokenInvalidated);
        await OtherRoutesAsync(sdk, official, state.Partition, McpCallerTools.SubscriptionsComplete, delivery,
            ErrorCode.TokenInvalidated, cancellationToken).ConfigureAwait(false);
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ReceiveSubscriptionAsync(receive,
            cancellationToken).ConfigureAwait(false), ErrorCode.DispatchPaused);
        await OtherRoutesAsync(sdk, official, state.Partition, McpCallerTools.SubscriptionsReceive, receive,
            ErrorCode.DispatchPaused, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingReadOracle.RequireAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
    }

    private static async Task OtherRoutesAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        string tool, object request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(tool, request, cancellationToken).ConfigureAwait(false),
            expected, dispatched: true);
        var sql = SqlRf3Protocol.Call(partition, tool, request, ClusterRestoreRf3EventingReadOracle.CommandId(request));
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(sql,
            cancellationToken).ConfigureAwait(false), expected);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName,
            sql, cancellationToken).ConfigureAwait(false), expected, dispatched: true);
    }
}
