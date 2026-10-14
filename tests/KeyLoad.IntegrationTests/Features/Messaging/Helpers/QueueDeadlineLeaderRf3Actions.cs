using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Actions
{
    internal static async Task PublishAsync(RequestCqrsRf3Callers admin, QueueDeadlineLeaderRf3State state,
        bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueDeadlineLeaderRf3Protocol.Healthy : QueueDeadlineLeaderRf3Protocol.Message;
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, id, QueueDeadlineRf3Protocol.Payload,
                QueueDeadlineRf3Protocol.Headers, NotBefore: healthy ? null : state.Due)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await QueueDeadlineLeaderRf3Expected.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueDeadlineLeaderRf3Protocol.EnqueueKind, state.Lane.Queue, id, 1L)]);
        if (healthy)
        { state.Healthy = QueueDeadlineLeaderRf3Expected.Ready(true); }
        else
        { state.Original = QueueDeadlineLeaderRf3Expected.Scheduled(state.Due); }
        state.Producers.Add((command, receipt));
        await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
        await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
    }

    internal static async Task StaleAsync(RequestCqrsRf3Callers admin, QueueDeadlineLeaderRf3State state,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new AdvanceQueueDeadline(state.Lane.Queue, QueueDeadlineLeaderRf3Protocol.Message,
                MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);
        var result = await admin.Sdk.CommitAsync(command, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
            QueueDeadlineLeaderRf3Protocol.StaleDetail), result.Problem);
        await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
        var reply = await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(QueueDeadlineLeaderRf3Protocol.DetailKey).GetString())
            .IsEqualTo(QueueDeadlineLeaderRf3Protocol.SafeStaleDetail);
        await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueDeadlineLeaderRf3State state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Producers)
        {
            await QueueDeadlineLeaderRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(command, token)), receipt);
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
        }
        foreach (var (command, receipt) in state.Acks)
        {
            await QueueDeadlineLeaderRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await worker.Sdk.CompleteAsync(command, token)), receipt);
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, receipt);
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
        }
    }
}
