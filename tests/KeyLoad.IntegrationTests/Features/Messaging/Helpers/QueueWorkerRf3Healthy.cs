using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Healthy
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        CancellationToken token)
    {
        var retained = await QueueWorkerRf3HealthyImages.CaptureAsync(sdk, mcp, seed, token);
        var producer = new CommandRequest(Guid.NewGuid(), seed.Lane.Partition,
            [new EnqueueMessage(seed.Lane.Queue, QueueWorkerRf3Protocol.Healthy,
                QueueWorkerRf3Protocol.Payload, QueueWorkerRf3Protocol.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(producer, token));
        await QueueWorkerRf3Assertions.ReceiptAsync(receipt, producer.CommandId, seed,
            [new(QueueWorkerRf3Protocol.Enqueue, seed.Lane.Queue, QueueWorkerRf3Protocol.Healthy, QueueWorkerRf3Protocol.ReadyStateVersion)]);
        await QueueWorkerRf3HealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Healthy,
            QueueWorkerRf3Protocol.HealthyReadySequence, token);
        QueueWorkerDeliveryContext? context = null;
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (current, handlerToken) =>
        {
            handlerToken.ThrowIfCancellationRequested();
            context = current;
            await QueueWorkerRf3HealthyImages.RetainedAsync(sdk, mcp, seed, retained, handlerToken);
            await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, current,
                QueueWorkerRf3Protocol.HealthyReadySequence, handlerToken);
        }, QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack)).HasSingleItem();
        ArgumentNullException.ThrowIfNull(context);
        await QueueWorkerRf3Claims.RequireAsync(seed, context.Delivery);
        await QueueWorkerRf3Assertions.EqualAsync(context.Delivery, new Delivery(QueueWorkerRf3Protocol.Healthy,
            QueueWorkerRf3Protocol.Payload, QueueWorkerRf3Protocol.Headers, context.Delivery.Token,
            QueueWorkerRf3Protocol.InitialLeaseVersion, context.Delivery.LeaseUntil, QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.InitialDeliveryGeneration));
        var ackReceipt = await QueueWorkerRf3Completion.SuccessAsync(original);
        await QueueWorkerRf3Assertions.ReceiptAsync(ackReceipt, context.AcknowledgementId, seed,
            [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, QueueWorkerRf3Protocol.Healthy, QueueWorkerRf3Protocol.AckStateVersion)]);
        await QueueWorkerRf3HealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Healthy,
            QueueWorkerRf3Protocol.HealthyReadySequence, token);
        await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Completion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(original, token)),
            ackReceipt);
        await QueueWorkerRf3HealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Healthy,
            QueueWorkerRf3Protocol.HealthyReadySequence, token);
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, original.Command!, token));
        await QueueWorkerRf3Assertions.EqualAsync(official.Value, ackReceipt);
        await QueueWorkerRf3HealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Healthy,
            QueueWorkerRf3Protocol.HealthyReadySequence, token);
    }
}
