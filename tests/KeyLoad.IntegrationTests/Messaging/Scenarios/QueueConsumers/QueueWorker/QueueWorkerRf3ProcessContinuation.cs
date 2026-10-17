using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ProcessContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, DeliveryCommand parentAck, CommitReceipt original, CancellationToken token)
    {
        var fresh = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerRf3Claims.LiteralAsync(seed, context.Delivery, handlerToken),
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(fresh.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(fresh.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.RemainingReadyDeliveries);
        await Assert.That(fresh.Unacknowledged).IsEmpty();
        await Assert.That(fresh.Failures).IsEmpty();
        await OriginalsAsync(sdk, mcp, seed, parentAck, original, fresh, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await OriginalsAsync(sdk, mcp, seed, parentAck, original, fresh, token);
        await QueueWorkerRf3Healthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task OriginalsAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        DeliveryCommand parentAck, CommitReceipt original, QueueWorkerResult fresh, CancellationToken token)
    {
        await ImagesAsync(sdk, mcp, seed, token);
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CompleteAsync(parentAck, token)), original);
        await ImagesAsync(sdk, mcp, seed, token);
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, parentAck, token))).Value, original);
        await ImagesAsync(sdk, mcp, seed, token);
        foreach (var ack in fresh.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack))
        {
            var receipt = await QueueWorkerRf3Completion.SuccessAsync(ack);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            await Assert.That(mutation.Id).IsNotEqualTo(QueueWorkerRf3Protocol.First);
            await QueueWorkerRf3Assertions.ReceiptAsync(receipt, ack.Command!.CommandId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, mutation.Id, QueueWorkerRf3Protocol.AckStateVersion)]);
            await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Completion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(ack, token)),
                receipt);
            await ImagesAsync(sdk, mcp, seed, token);
            await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command, token))).Value, receipt);
            await ImagesAsync(sdk, mcp, seed, token);
        }
    }

    private static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        CancellationToken token)
    {
        foreach (var id in QueueWorkerRf3Protocol.Messages)
        {
            var sequence = Array.IndexOf(QueueWorkerRf3Protocol.Messages, id) + QueueWorkerRf3Protocol.ReadySequenceOffset;
            await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
        }
    }
}
