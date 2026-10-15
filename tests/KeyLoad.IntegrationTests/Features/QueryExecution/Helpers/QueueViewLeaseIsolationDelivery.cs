using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationDelivery
{
    internal static Task ActiveReplayAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            var request = state.Claim ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
            var original = state.OriginalDelivery ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
            await SqlRf3Protocol.EqualAsync(original, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReceiveAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.McpAsync<ReceiveResult>(root.Mcp,
                SqlRf3Protocol.Call(state.Scenario.Partition, McpCallerTools.MessagesReceive, request), token));
        }, token);

    internal static async Task RenewAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var before = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), state.Scenario.Lane, state.Delivery.Token,
            DeliveryAction.Renew, LeaseSeconds: QueueViewLeaseIsolationProtocol.LeaseSeconds);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CompleteAsync(command, token));
        await ReceiptAsync(root, state, command, receipt, QueueViewLeaseIsolationProtocol.RenewedVersion, token);
        state.Version = QueueViewLeaseIsolationProtocol.RenewedVersion;
        var after = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var first = after.Messages[QueueViewLeaseIsolationProtocol.Empty];
        await Assert.That(first.Metadata.LeaseUntil > state.Delivery.LeaseUntil).IsTrue();
        await SqlRf3Protocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.Empty] with
        {
            Metadata = before.Messages[QueueViewLeaseIsolationProtocol.Empty].Metadata with
            {
                StateVersion = QueueViewLeaseIsolationProtocol.RenewedVersion,
                LeaseUntil = first.Metadata.LeaseUntil
            }
        }, first);
        await SqlRf3Protocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.One], after.Messages[QueueViewLeaseIsolationProtocol.One]);
        await ActiveReplayAsync(root, state, token);
    }

    internal static async Task AckAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var before = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), state.Scenario.Lane, state.Delivery.Token, DeliveryAction.Ack);
        var receipt = await SqlRf3Protocol.McpAsync<CommitReceipt>(root.Mcp,
            SqlRf3Protocol.Call(state.Scenario.Partition, McpCallerTools.MessagesComplete, command), token);
        await ReceiptAsync(root, state, command, receipt, QueueViewLeaseIsolationProtocol.AckedVersion, token);
        state.Version = QueueViewLeaseIsolationProtocol.AckedVersion;
        state.Acked = true;
        var after = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        await SqlRf3Protocol.EqualAsync(new MessageInspection(before.Messages[QueueViewLeaseIsolationProtocol.Empty].Metadata with
        {
            State = MessageState.Acked,
            StateVersion = QueueViewLeaseIsolationProtocol.AckedVersion,
            LeaseOwner = null,
            LeaseUntil = null
        }, null, null), after.Messages[QueueViewLeaseIsolationProtocol.Empty]);
        await SqlRf3Protocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.One], after.Messages[QueueViewLeaseIsolationProtocol.One]);
    }

    private static async Task ReceiptAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state,
        DeliveryCommand command, CommitReceipt receipt, long version, CancellationToken token)
    {
        await QueueViewLeaseIsolationReceipts.AssertAsync(root, state.Scenario, receipt, command.CommandId,
            [new(command.Action.ToString(), SqlModelViewRf3Scenario.Queue, SqlModelViewRf3Scenario.FirstMessageId, version)], token);
        state.Receipts.Add(new(command, receipt));
    }
}
