using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationDelivery
{
    internal static Task ActiveReplayAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            var request = state.Claim ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
            var original = state.OriginalDelivery ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
            await SqlClusterProtocol.EqualAsync(original, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReceiveAsync(request, token)));
            await SqlClusterProtocol.EqualAsync(original, await SqlClusterProtocol.McpAsync<ReceiveResult>(root.Mcp,
                SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.MessagesReceive, request), token));
        }, token);

    internal static async Task RenewAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var before = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), state.Scenario.Lane, state.Delivery.Token,
            DeliveryAction.Renew, LeaseSeconds: QueueViewLeaseIsolationProtocol.LeaseSeconds);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CompleteAsync(command, token));
        await ReceiptAsync(state, command, receipt, QueueViewLeaseIsolationProtocol.RenewedVersion);
        state.Version = QueueViewLeaseIsolationProtocol.RenewedVersion;
        var after = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var first = after.Messages[QueueViewLeaseIsolationProtocol.Empty];
        await Assert.That(first.Metadata.LeaseUntil > state.Delivery.LeaseUntil).IsTrue();
        await SqlClusterProtocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.Empty] with
        {
            Metadata = before.Messages[QueueViewLeaseIsolationProtocol.Empty].Metadata with
            {
                StateVersion = QueueViewLeaseIsolationProtocol.RenewedVersion,
                LeaseUntil = first.Metadata.LeaseUntil
            }
        }, first);
        await SqlClusterProtocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.One], after.Messages[QueueViewLeaseIsolationProtocol.One]);
        await ActiveReplayAsync(root, state, token);
    }

    internal static async Task AckAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var before = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), state.Scenario.Lane, state.Delivery.Token, DeliveryAction.Ack);
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp,
            SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.MessagesComplete, command), token);
        await ReceiptAsync(state, command, receipt, QueueViewLeaseIsolationProtocol.AckedVersion);
        state.Version = QueueViewLeaseIsolationProtocol.AckedVersion;
        state.Acked = true;
        var after = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        await SqlClusterProtocol.EqualAsync(new MessageInspection(before.Messages[QueueViewLeaseIsolationProtocol.Empty].Metadata with
        {
            State = MessageState.Acked,
            StateVersion = QueueViewLeaseIsolationProtocol.AckedVersion,
            LeaseOwner = null,
            LeaseUntil = null
        }, null, null), after.Messages[QueueViewLeaseIsolationProtocol.Empty]);
        await SqlClusterProtocol.EqualAsync(before.Messages[QueueViewLeaseIsolationProtocol.One], after.Messages[QueueViewLeaseIsolationProtocol.One]);
    }

    private static async Task ReceiptAsync(QueueViewLeaseIsolationState state,
        DeliveryCommand command, CommitReceipt receipt, long version)
    {
        await QueueViewLeaseIsolationReceipts.AssertAsync(state.Scenario, receipt, command.CommandId,
            [new(command.Action.ToString(), SqlModelViewClusterScenario.Queue, SqlModelViewClusterScenario.FirstMessageId, version)]);
        state.Receipts.Add(new(command, receipt));
    }
}
