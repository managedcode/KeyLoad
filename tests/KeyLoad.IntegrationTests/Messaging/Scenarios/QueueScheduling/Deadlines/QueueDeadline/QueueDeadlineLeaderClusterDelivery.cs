using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterDelivery
{
    internal static async Task SettleAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDeadlineLeaderClusterState state, bool healthy,
        CancellationToken token)
    {
        var ready = healthy ? state.Healthy! : state.Original!;
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane,
            LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var received = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Token.AtomicPartitionId).IsEqualTo(state.Lane.Partition.AtomicPartitionId);
        await Assert.That(received.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(ready.Metadata.Id);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineClusterProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueDeadlineClusterProtocol.Headers);
        await Assert.That(delivery.Token).IsNotEmpty();
        Set(state, new(ready.Metadata with
        {
            State = MessageState.Leased,
            Attempts = 1,
            StateVersion = ready.Metadata.StateVersion + 1,
            LeaseOwner = state.Identity.Principal.Id,
            LeaseVersion = 1L,
            LeaseUntil = delivery.LeaseUntil
        }, QueueDeadlineClusterProtocol.Payload, QueueDeadlineClusterProtocol.Headers), healthy);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value, received);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(command, token));
        var version = healthy ? 3L : 4L;
        await QueueDeadlineLeaderClusterExpected.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueDeadlineLeaderClusterProtocol.AckKind, state.Lane.Queue, delivery.Id, version)]);
        Set(state, new(new(delivery.Id, MessageState.Acked, 1, version, healthy ? 2L : 1L,
            null, null, LeaseVersion: 1L), null, null), healthy);
        state.Acks.Add((command, receipt));
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, receipt);
        await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
    }

    private static void Set(QueueDeadlineLeaderClusterState state, MessageInspection value, bool healthy)
    {
        if (healthy)
        {
            state.Healthy = value;
        }
        else
        {
            state.Original = value;
        }
    }
}
