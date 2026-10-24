using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDueDatabaseScopeClusterActions
{
    internal static async Task PublishAsync(RequestCqrsClusterCallers admin, QueueDueDatabaseScopeClusterState state,
        QueueDueDatabaseScopeClusterLane lane, bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueDueDatabaseScopeClusterProtocol.Healthy : QueueDueDatabaseScopeClusterProtocol.Message;
        var command = new CommandRequest(Guid.NewGuid(), lane.Lane.Partition,
            [new EnqueueMessage(lane.Lane.Queue, id, lane.Payload,
                QueueDeadlineClusterProtocol.Headers, NotBefore: healthy ? null : state.Due)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await QueueDueDatabaseScopeClusterExpected.ReceiptAsync(receipt, command.CommandId, lane,
            [new(QueueDueDatabaseScopeClusterProtocol.EnqueueKind, lane.Lane.Queue, id, 1L)]);
        if (healthy)
        {
            lane.Healthy = QueueDueDatabaseScopeClusterExpected.Ready(lane, true);
        }
        else
        {
            lane.Message = QueueDueDatabaseScopeClusterExpected.Scheduled(lane, state.Due);
        }
        lane.Seeds.Add((command, receipt));
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task RefuseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDueDatabaseScopeClusterState state, bool oldAck, CancellationToken token)
    {
        var detail = oldAck ? QueueDueDatabaseScopeClusterProtocol.PolicyChanged : QueueDueDatabaseScopeClusterProtocol.ScopeDenied;
        if (oldAck)
        {
            var command = state.Allowed.Acks.Single(item => item.PolicyEpoch == 1L).Command;
            await RequireAsync(admin, state, await worker.Sdk.CompleteAsync(command, token), detail, token);
            var reply = await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        }
        else
        {
            var lane = state.Denied.Lane;
            var command = new CommandRequest(Guid.NewGuid(), lane.Partition,
                [new AdvanceQueueDeadline(lane.Queue, QueueDueDatabaseScopeClusterProtocol.Message,
                    MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);
            await RequireAsync(admin, state, await worker.Sdk.CommitAsync(command, token), detail, token);
            var reply = await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        }
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
    }

    private static async Task RequireAsync(RequestCqrsClusterCallers admin, QueueDueDatabaseScopeClusterState state,
        ManagedCode.Communication.Result<CommitReceipt> result, string detail, CancellationToken token)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, detail), result.Problem);
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDueDatabaseScopeClusterState state, CancellationToken token)
    {
        foreach (var lane in state.Lanes)
        {
            foreach (var (command, result) in lane.Seeds)
            {
                await QueueDueDatabaseScopeClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.CommitAsync(command, token)), result);
                await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
                await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, result);
                await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
            }
            foreach (var (command, result, epoch) in lane.Acks)
            {
                if (epoch != state.Identity.Principal.PolicyEpoch)
                {
                    continue;
                }
                await QueueDueDatabaseScopeClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await worker.Sdk.CompleteAsync(command, token)), result);
                await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
                await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, result);
                await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
            }
        }
    }
}
