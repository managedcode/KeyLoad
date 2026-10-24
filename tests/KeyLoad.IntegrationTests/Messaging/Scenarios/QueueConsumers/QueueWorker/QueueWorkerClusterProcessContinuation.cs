using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterProcessContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, DeliveryCommand parentAck, CommitReceipt original, CancellationToken token)
    {
        var fresh = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerClusterClaims.LiteralAsync(seed, context.Delivery, handlerToken),
            QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(fresh.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(fresh.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.RemainingReadyDeliveries);
        await Assert.That(fresh.Unacknowledged).IsEmpty();
        await Assert.That(fresh.Failures).IsEmpty();
        await OriginalsAsync(sdk, mcp, seed, parentAck, original, fresh, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await OriginalsAsync(sdk, mcp, seed, parentAck, original, fresh, token);
        await QueueWorkerClusterHealthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task OriginalsAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        DeliveryCommand parentAck, CommitReceipt original, QueueWorkerResult fresh, CancellationToken token)
    {
        await ImagesAsync(sdk, mcp, seed, token);
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CompleteAsync(parentAck, token)), original);
        await ImagesAsync(sdk, mcp, seed, token);
        await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, parentAck, token))).Value, original);
        await ImagesAsync(sdk, mcp, seed, token);
        foreach (var ack in fresh.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack))
        {
            var receipt = await QueueWorkerClusterCompletion.SuccessAsync(ack);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            await Assert.That(mutation.Id).IsNotEqualTo(QueueWorkerClusterProtocol.First);
            await QueueWorkerClusterAssertions.ReceiptAsync(receipt, ack.Command!.CommandId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, mutation.Id, QueueWorkerClusterProtocol.AckStateVersion)]);
            await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(ack, token)),
                receipt);
            await ImagesAsync(sdk, mcp, seed, token);
            await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command, token))).Value, receipt);
            await ImagesAsync(sdk, mcp, seed, token);
        }
    }

    private static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        CancellationToken token)
    {
        foreach (var id in QueueWorkerClusterProtocol.Messages)
        {
            var sequence = Array.IndexOf(QueueWorkerClusterProtocol.Messages, id) + QueueWorkerClusterProtocol.ReadySequenceOffset;
            await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
        }
    }
}
