using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateQueueExercise
{
    internal static async Task RunAsync(FunctionalGateContext ctx, int iteration, int resourceIndex, CancellationToken token)
    {
        var lane = new QueueLaneRef(ctx.Partition, FunctionalGateQueues.Queue(resourceIndex));
        var id = FunctionalGateQueues.Id(iteration);
        var payload = FunctionalGateQueues.Payload(ctx, iteration);
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [new EnqueueMessage(lane.Queue, id, payload)]);
        await RefuseAsync(ctx, command, token);
        var receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateQueues.ReceiptAsync(ctx, receipt, command.CommandId, new("enqueue", lane.Queue, id, 1));
        await FunctionalGateQueues.EqualAsync(receipt,
            await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token));
        var original = await InspectAsync(ctx, lane, id, token);
        await Assert.That(original.Metadata.ReadySequence).IsGreaterThan(0);
        await FunctionalGateQueues.EqualAsync(new MessageInspection(new(id, MessageState.Ready, 0, 1,
            original.Metadata.ReadySequence, null, null), payload, "{}"), original);
        var first = await ClaimAsync(ctx, lane, id, payload, 1, 1, token);
        var nack = new DeliveryCommand(Guid.NewGuid(), lane, first.Token, DeliveryAction.Nack);
        var rejected = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.MessagesComplete, nack, token);
        await FunctionalGateQueues.ReceiptAsync(ctx, rejected, nack.CommandId, new("Nack", lane.Queue, id, 3));
        await FunctionalGateQueues.EqualAsync(rejected, await ctx.SdkAsync(ctx.Writer.Sdk.CompleteAsync(nack, token)));
        var parked = await InspectAsync(ctx, lane, id, token);
        await Assert.That(parked.Metadata.ParkedSequence).IsGreaterThan(0);
        await FunctionalGateQueues.EqualAsync(new MessageInspection(original.Metadata with
        {
            State = MessageState.DeadLettered,
            Attempts = 1,
            StateVersion = 3,
            LeaseVersion = 1,
            SafeFailureCode = "AttemptsExhausted",
            ParkedSequence = parked.Metadata.ParkedSequence
        }, payload, "{}"), parked);
        var redrive = new CommandRequest(Guid.NewGuid(), ctx.Partition, [new RedriveQueueMessage(lane.Queue, id, 3, 1)]);
        await RefuseRedriveAsync(ctx, redrive, lane, id, parked, token);
        var redriven = await ctx.McpAsync<CommitReceipt>(ctx.QueueAdministrator, McpCallerTools.DocumentsCommit, redrive, token);
        await FunctionalGateQueues.ReceiptAsync(ctx, redriven, redrive.CommandId, new("redriveQueueMessage", lane.Queue, id, 4));
        await FunctionalGateQueues.EqualAsync(redriven, await ctx.SdkAsync(ctx.QueueAdministrator.Sdk.CommitAsync(redrive, token)));
        var second = await ClaimAsync(ctx, lane, id, payload, 3, 2, token);
        await Assert.That(second.Token).IsNotEqualTo(first.Token);
        await StaleAsync(ctx, lane, first, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, second.Token, DeliveryAction.Ack);
        var acknowledged = await ctx.SdkAsync(ctx.Writer.Sdk.CompleteAsync(ack, token));
        await FunctionalGateQueues.ReceiptAsync(ctx, acknowledged, ack.CommandId, new("Ack", lane.Queue, id, 6));
        await FunctionalGateQueues.EqualAsync(acknowledged,
            await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.MessagesComplete, ack, token));
        var terminal = await InspectAsync(ctx, lane, id, token);
        await FunctionalGateQueues.EqualAsync(new MessageInspection(new(id, MessageState.Acked, 1, 6,
            original.Metadata.ReadySequence + 1, null, null, LeaseVersion: 3, DeliveryGeneration: 2), null, null), terminal);
        await FunctionalGateQueues.EqualAsync(receipt, await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token)));
        await FunctionalGateQueues.EqualAsync(terminal, await InspectAsync(ctx, lane, id, token));
    }

    private static async Task<Delivery> ClaimAsync(FunctionalGateContext ctx, QueueLaneRef lane, string id,
        string payload, long leaseVersion, long generation, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var result = await ctx.SdkAsync(ctx.Writer.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(leaseVersion);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(generation);
        await Assert.That(delivery.Token).IsNotEmpty();
        await FunctionalGateQueues.EqualAsync(result,
            await ctx.McpAsync<ReceiveResult>(ctx.Writer, McpCallerTools.MessagesReceive, request, token));
        return delivery;
    }

    private static async Task<MessageInspection> InspectAsync(FunctionalGateContext ctx, QueueLaneRef lane,
        string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var sdk = await ctx.SdkAsync(ctx.Writer.Sdk.InspectAsync(request, token));
        await FunctionalGateQueues.EqualAsync(sdk,
            await ctx.McpAsync<MessageInspectionResult>(ctx.Writer, McpCallerTools.MessagesInspect, request, token));
        return sdk.Message ?? throw new InvalidOperationException("The gate message is missing.");
    }

    private static async Task RefuseAsync(FunctionalGateContext ctx, CommandRequest command, CancellationToken token)
    {
        var denied = await ctx.Reader.Sdk.CommitAsync(command with
        {
            CommandId = Guid.NewGuid()
        }, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await ctx.Reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command with
        {
            CommandId = Guid.NewGuid()
        }, token),
            ErrorCode.PermissionDenied, dispatched: true)).Value;
        var id = ((EnqueueMessage)command.Mutations[0]).MessageId;
        var absent = await ctx.SdkAsync(ctx.Writer.Sdk.InspectAsync(new(new(ctx.Partition, command.Mutations[0].Resource), id), token));
        await FunctionalGateQueues.EqualAsync(new MessageInspectionResult(null), absent);
    }

    private static async Task RefuseRedriveAsync(FunctionalGateContext ctx, CommandRequest command,
        QueueLaneRef lane, string id, MessageInspection parked, CancellationToken token)
    {
        var refused = command with
        {
            CommandId = Guid.NewGuid()
        };
        var denied = await ctx.Writer.Sdk.CommitAsync(refused, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await ctx.Writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit, refused, token),
            ErrorCode.PermissionDenied, dispatched: true)).Value;
        await FunctionalGateQueues.EqualAsync(parked, await InspectAsync(ctx, lane, id, token));
    }

    private static async Task StaleAsync(FunctionalGateContext ctx, QueueLaneRef lane, Delivery old, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), lane, old.Token, DeliveryAction.Ack);
        var denied = await ctx.Writer.Sdk.CompleteAsync(command, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.StaleLease.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await ctx.Writer.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token),
            ErrorCode.StaleLease, dispatched: true)).Value;
    }
}
