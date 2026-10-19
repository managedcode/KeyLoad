using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeClusterDelivery
{
    internal static async Task SettleAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueDueDatabaseScopeClusterState state, QueueDueDatabaseScopeClusterLane lane, bool healthy,
        CancellationToken token)
    {
        var ready = healthy ? lane.Healthy! : lane.Message!;
        var request = new ReceiveRequest(Guid.NewGuid(), lane.Lane,
            LeaseSeconds: QueueDueDatabaseScopeClusterProtocol.LeaseSeconds);
        var received = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Token.AtomicPartitionId).IsEqualTo(lane.Lane.Partition.AtomicPartitionId);
        await Assert.That(received.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(ready.Metadata.Id);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.PayloadJson).IsEqualTo(lane.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueDeadlineClusterProtocol.Headers);
        await Assert.That(delivery.Token).IsNotEmpty();
        Set(lane, new(ready.Metadata with
        {
            State = MessageState.Leased,
            Attempts = 1,
            StateVersion = ready.Metadata.StateVersion + 1,
            LeaseOwner = state.Identity.Principal.Id,
            LeaseVersion = 1L,
            LeaseUntil = delivery.LeaseUntil
        }, lane.Payload, QueueDeadlineClusterProtocol.Headers), healthy);
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value, received);
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
        var command = new DeliveryCommand(Guid.NewGuid(), lane.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(command, token));
        var version = healthy ? 3L : 4L;
        await QueueDueDatabaseScopeClusterExpected.ReceiptAsync(receipt, command.CommandId, lane,
            [new(QueueDueDatabaseScopeClusterProtocol.AckKind, lane.Lane.Queue, delivery.Id, version)]);
        Set(lane, new(new(delivery.Id, MessageState.Acked, 1, version, healthy ? 2L : 1L,
            null, null, LeaseVersion: 1L), null, null), healthy);
        lane.Acks.Add((command, receipt, state.Identity.Principal.PolicyEpoch));
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
        await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token))).Value, receipt);
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
    }

    private static void Set(QueueDueDatabaseScopeClusterLane lane, MessageInspection value, bool healthy)
    {
        if (healthy)
        {
            lane.Healthy = value;
        }
        else
        {
            lane.Message = value;
        }
    }
}
