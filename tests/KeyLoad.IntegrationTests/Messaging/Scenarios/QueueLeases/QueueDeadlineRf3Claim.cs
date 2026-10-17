using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Claim
{
    internal static async Task<QueueDeadlineRf3Acknowledged> CompleteAsync(RequestCqrsRf3Callers worker,
        QueueLaneRef lane, MessageInspection ready, CancellationToken token)
    {
        var claim = new ReceiveRequest(Guid.NewGuid(), lane);
        var result = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, claim, token))).Value;
        await Assert.That(result.Deliveries.Length).IsEqualTo((int)QueueDeadlineRf3Protocol.First);
        var delivery = result.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(ready.Metadata.Id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineRf3Protocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueDeadlineRf3Protocol.Headers);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(QueueDeadlineRf3Protocol.First);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueDeadlineRf3Protocol.First);
        await Assert.That(delivery.Attempt).IsEqualTo((int)QueueDeadlineRf3Protocol.First);
        await QueueScheduledClockRf3Replay.ReceiveAsync(worker, claim, result, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(ack, token));
        await QueueScheduledClockRf3Replay.AckAsync(worker, ack, acknowledged, token);
        var terminal = ready with
        {
            Metadata = ready.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = ready.Metadata.StateVersion + QueueDeadlineRf3Protocol.Second,
                Attempts = (int)QueueDeadlineRf3Protocol.First,
                LeaseVersion = QueueDeadlineRf3Protocol.First
            },
            PayloadJson = null,
            HeadersJson = null
        };
        await QueueDeadlineRf3Assertions.RequireAsync(worker, lane, terminal, token);
        return new(ack, acknowledged, terminal);
    }
}
