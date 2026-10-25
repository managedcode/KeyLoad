using KeyLoad.ClusterTests.ClientApi;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateQueueGraphProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateQueueGraphQueue
{
    internal static async Task<MessageInspection> ReadyAsync(FunctionalGateContext ctx, QueueLaneRef lane,
        string id, string payload, CancellationToken token)
    {
        var actual = await InspectAsync(ctx, lane, id, token);
        await Assert.That(actual.Metadata.ReadySequence).IsGreaterThan(P.Absent);
        await FunctionalGateQueues.EqualAsync(new MessageInspection(new(id, MessageState.Ready, 0, P.Created,
            actual.Metadata.ReadySequence, null, null), payload, P.EmptyHeaders), actual);
        return actual;
    }

    internal static async Task ConsumeAsync(FunctionalGateContext ctx, QueueLaneRef lane,
        MessageInspection original, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane, P.MaximumMessages);
        var received = await ctx.SdkAsync(ctx.Writer.Sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await FunctionalGateQueues.EqualAsync(received,
            await ctx.McpAsync<ReceiveResult>(ctx.Writer, McpCallerTools.MessagesReceive, request, token));
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(original.Metadata.Id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(original.PayloadJson);
        await Assert.That(delivery.HeadersJson).IsEqualTo(P.EmptyHeaders);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(P.Created);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(P.Created);
        await Assert.That(delivery.Token).IsNotEmpty();
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.MessagesComplete, ack, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx, receipt, ack.CommandId,
            [new(P.AckKind, lane.Queue, delivery.Id, P.Acknowledged)]);
        await FunctionalGateQueues.EqualAsync(receipt, await ctx.SdkAsync(ctx.Writer.Sdk.CompleteAsync(ack, token)));
        await FunctionalGateQueues.EqualAsync(new MessageInspection(new(delivery.Id, MessageState.Acked, 1,
            P.Acknowledged, original.Metadata.ReadySequence, null, null, LeaseVersion: P.Created), null, null),
            await InspectAsync(ctx, lane, delivery.Id, token));
    }

    private static async Task<MessageInspection> InspectAsync(FunctionalGateContext ctx, QueueLaneRef lane,
        string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var actual = await ctx.SdkAsync(ctx.Writer.Sdk.InspectAsync(request, token));
        await FunctionalGateQueues.EqualAsync(actual,
            await ctx.McpAsync<MessageInspectionResult>(ctx.Writer, McpCallerTools.MessagesInspect, request, token));
        return actual.Message ?? throw new InvalidOperationException();
    }
}
