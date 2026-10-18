using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterActions
{
    internal static async Task PublishAsync(RequestCqrsClusterCallers admin, QueueDeadlineLeaderClusterState state,
        bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueDeadlineLeaderClusterProtocol.Healthy : QueueDeadlineLeaderClusterProtocol.Message;
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, id, QueueDeadlineClusterProtocol.Payload,
                QueueDeadlineClusterProtocol.Headers, NotBefore: healthy ? null : state.Due)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await QueueDeadlineLeaderClusterExpected.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueDeadlineLeaderClusterProtocol.EnqueueKind, state.Lane.Queue, id, 1L)]);
        if (healthy)
        {
            state.Healthy = QueueDeadlineLeaderClusterExpected.Ready(true);
        }
        else
        {
            state.Original = QueueDeadlineLeaderClusterExpected.Scheduled(state.Due);
        }
        state.Producers.Add((command, receipt));
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task StaleAsync(RequestCqrsClusterCallers admin, QueueDeadlineLeaderClusterState state,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new AdvanceQueueDeadline(state.Lane.Queue, QueueDeadlineLeaderClusterProtocol.Message,
                MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);
        var result = await admin.Sdk.CommitAsync(command, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
            QueueDeadlineLeaderClusterProtocol.StaleDetail), result.Problem);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        var reply = await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(QueueDeadlineLeaderClusterProtocol.DetailKey).GetString())
            .IsEqualTo(QueueDeadlineLeaderClusterProtocol.SafeStaleDetail);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDeadlineLeaderClusterState state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Producers)
        {
            await QueueDeadlineLeaderClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(command, token)), receipt);
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        }
        foreach (var (command, receipt) in state.Acks)
        {
            await QueueDeadlineLeaderClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await worker.Sdk.CompleteAsync(command, token)), receipt);
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, receipt);
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        }
    }
}
