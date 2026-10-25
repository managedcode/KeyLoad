using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadlineClusterClaim
{
    internal static async Task<QueueDeadlineClusterAcknowledged> CompleteAsync(RequestCqrsClusterCallers worker,
        QueueLaneRef lane, MessageInspection ready, CancellationToken token)
    {
        var claim = new ReceiveRequest(Guid.NewGuid(), lane);
        var result = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, claim, token))).Value;
        await Assert.That(result.Deliveries.Length).IsEqualTo((int)QueueDeadlineClusterProtocol.First);
        var delivery = result.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(ready.Metadata.Id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineClusterProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueDeadlineClusterProtocol.Headers);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(QueueDeadlineClusterProtocol.First);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueDeadlineClusterProtocol.First);
        await Assert.That(delivery.Attempt).IsEqualTo((int)QueueDeadlineClusterProtocol.First);
        await QueueScheduledClockClusterReplay.ReceiveAsync(worker, claim, result, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(ack, token));
        await QueueScheduledClockClusterReplay.AckAsync(worker, ack, acknowledged, token);
        var terminal = ready with
        {
            Metadata = ready.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = ready.Metadata.StateVersion + QueueDeadlineClusterProtocol.Second,
                Attempts = (int)QueueDeadlineClusterProtocol.First,
                LeaseVersion = QueueDeadlineClusterProtocol.First
            },
            PayloadJson = null,
            HeadersJson = null
        };
        await QueueDeadlineClusterAssertions.RequireAsync(worker, lane, terminal, token);
        return new(ack, acknowledged, terminal);
    }
}
