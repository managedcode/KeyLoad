using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeRf3Actions
{
    internal static async Task PublishAsync(RequestCqrsRf3Callers admin, QueueDueDatabaseScopeRf3State state,
        QueueDueDatabaseScopeRf3Lane lane, bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueDueDatabaseScopeRf3Protocol.Healthy : QueueDueDatabaseScopeRf3Protocol.Message;
        var command = new CommandRequest(Guid.NewGuid(), lane.Lane.Partition,
            [new EnqueueMessage(lane.Lane.Queue, id, lane.Payload,
                QueueDeadlineRf3Protocol.Headers, NotBefore: healthy ? null : state.Due)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await QueueDueDatabaseScopeRf3Expected.ReceiptAsync(receipt, command.CommandId, lane,
            [new(QueueDueDatabaseScopeRf3Protocol.EnqueueKind, lane.Lane.Queue, id, 1L)]);
        if (healthy)
        {
            lane.Healthy = QueueDueDatabaseScopeRf3Expected.Ready(lane, true);
        }
        else
        {
            lane.Message = QueueDueDatabaseScopeRf3Expected.Scheduled(lane, state.Due);
        }
        lane.Seeds.Add((command, receipt));
        await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
        await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueDueDatabaseScopeRf3State state, bool oldAck, CancellationToken token)
    {
        var detail = oldAck ? QueueDueDatabaseScopeRf3Protocol.PolicyChanged : QueueDueDatabaseScopeRf3Protocol.ScopeDenied;
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
                [new AdvanceQueueDeadline(lane.Queue, QueueDueDatabaseScopeRf3Protocol.Message,
                    MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);
            await RequireAsync(admin, state, await worker.Sdk.CommitAsync(command, token), detail, token);
            var reply = await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        }
        await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
    }

    private static async Task RequireAsync(RequestCqrsRf3Callers admin, QueueDueDatabaseScopeRf3State state,
        ManagedCode.Communication.Result<CommitReceipt> result, string detail, CancellationToken token)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, detail), result.Problem);
        await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueDueDatabaseScopeRf3State state, CancellationToken token)
    {
        foreach (var lane in state.Lanes)
        {
            foreach (var (command, result) in lane.Seeds)
            {
                await QueueDueDatabaseScopeRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.CommitAsync(command, token)), result);
                await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
                await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, result);
                await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
            }
            foreach (var (command, result, epoch) in lane.Acks)
            {
                if (epoch != state.Identity.Principal.PolicyEpoch)
                {
                    continue;
                }
                await QueueDueDatabaseScopeRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await worker.Sdk.CompleteAsync(command, token)), result);
                await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
                await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, result);
                await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
            }
        }
    }
}
