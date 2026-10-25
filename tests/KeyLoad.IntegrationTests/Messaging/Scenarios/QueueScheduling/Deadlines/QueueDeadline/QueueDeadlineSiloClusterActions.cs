using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDeadlineSiloClusterActions
{
    internal static async Task PublishAsync(RequestCqrsClusterCallers admin, QueueDeadlineSiloClusterState state,
        bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueDeadlineSiloClusterProtocol.Healthy : QueueDeadlineSiloClusterProtocol.Message;
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, id, QueueDeadlineClusterProtocol.Payload,
                QueueDeadlineClusterProtocol.Headers, NotBefore: healthy ? null : state.Due)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await QueueDeadlineSiloClusterExpected.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueDeadlineSiloClusterProtocol.EnqueueKind, state.Lane.Queue, id, 1L)]);
        if (healthy)
        {
            state.Healthy = QueueDeadlineSiloClusterExpected.Ready(true);
        }
        else
        {
            state.Original = QueueDeadlineSiloClusterExpected.Scheduled(state.Due);
        }
        state.Producers.Add((command, receipt));
        await QueueDeadlineSiloClusterCurrent.OutboxAdvancedAsync(admin, state, token);
        await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDeadlineSiloClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task StaleAsync(RequestCqrsClusterCallers admin, QueueDeadlineSiloClusterState state,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new AdvanceQueueDeadline(state.Lane.Queue, QueueDeadlineSiloClusterProtocol.Message,
                MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);
        var result = await admin.Sdk.CommitAsync(command, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
            QueueDeadlineSiloClusterProtocol.StaleDetail), result.Problem);
        await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
        var reply = await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(QueueDeadlineSiloClusterProtocol.DetailKey).GetString())
            .IsEqualTo(QueueDeadlineSiloClusterProtocol.SafeStaleDetail);
        await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDeadlineSiloClusterState state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Producers)
        {
            await QueueDeadlineSiloClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(command, token)), receipt);
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineSiloClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
        }
        foreach (var (command, receipt) in state.Acks)
        {
            await QueueDeadlineSiloClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await worker.Sdk.CompleteAsync(command, token)), receipt);
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineSiloClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, receipt);
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
        }
    }
}
