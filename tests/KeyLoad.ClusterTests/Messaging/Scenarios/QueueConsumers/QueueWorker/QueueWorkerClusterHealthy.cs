using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterHealthy
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        CancellationToken token)
    {
        var retained = await QueueWorkerClusterHealthyImages.CaptureAsync(sdk, mcp, seed, token);
        var producer = new CommandRequest(Guid.NewGuid(), seed.Lane.Partition,
            [new EnqueueMessage(seed.Lane.Queue, QueueWorkerClusterProtocol.Healthy,
                QueueWorkerClusterProtocol.Payload, QueueWorkerClusterProtocol.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(producer, token));
        await QueueWorkerClusterAssertions.ReceiptAsync(receipt, producer.CommandId, seed,
            [new(QueueWorkerClusterProtocol.Enqueue, seed.Lane.Queue, QueueWorkerClusterProtocol.Healthy, QueueWorkerClusterProtocol.ReadyStateVersion)]);
        await QueueWorkerClusterHealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Healthy,
            QueueWorkerClusterProtocol.HealthyReadySequence, token);
        QueueWorkerDeliveryContext? context = null;
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (current, handlerToken) =>
        {
            handlerToken.ThrowIfCancellationRequested();
            context = current;
            await QueueWorkerClusterHealthyImages.RetainedAsync(sdk, mcp, seed, retained, handlerToken);
            await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, current,
                QueueWorkerClusterProtocol.HealthyReadySequence, handlerToken);
        }, QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.SingleDelivery);
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack)).HasSingleItem();
        ArgumentNullException.ThrowIfNull(context);
        await QueueWorkerClusterClaims.RequireAsync(seed, context.Delivery);
        await QueueWorkerClusterAssertions.EqualAsync(context.Delivery, new Delivery(QueueWorkerClusterProtocol.Healthy,
            QueueWorkerClusterProtocol.Payload, QueueWorkerClusterProtocol.Headers, context.Delivery.Token,
            QueueWorkerClusterProtocol.InitialLeaseVersion, context.Delivery.LeaseUntil, QueueWorkerClusterProtocol.ClaimAttempt, QueueWorkerClusterProtocol.InitialDeliveryGeneration));
        var ackReceipt = await QueueWorkerClusterCompletion.SuccessAsync(original);
        await QueueWorkerClusterAssertions.ReceiptAsync(ackReceipt, context.AcknowledgementId, seed,
            [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, QueueWorkerClusterProtocol.Healthy, QueueWorkerClusterProtocol.AckStateVersion)]);
        await QueueWorkerClusterHealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Healthy,
            QueueWorkerClusterProtocol.HealthyReadySequence, token);
        await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(original, token)),
            ackReceipt);
        await QueueWorkerClusterHealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Healthy,
            QueueWorkerClusterProtocol.HealthyReadySequence, token);
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, original.Command!, token));
        await QueueWorkerClusterAssertions.EqualAsync(official.Value, ackReceipt);
        await QueueWorkerClusterHealthyImages.RetainedAsync(sdk, mcp, seed, retained, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Healthy,
            QueueWorkerClusterProtocol.HealthyReadySequence, token);
    }
}
