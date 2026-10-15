using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ByteContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient admin, KeyLoadClient sdk,
        McpOfficialClient mcp, QueueWorkerRf3ByteSeed seed, Delivery first, CancellationToken token)
    {
        var result = await sdk.RunQueueAckWorkerAsync(seed.Native.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerRf3ByteImages.DeliveryAsync(seed, context.Delivery, handlerToken),
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.RemainingReadyDeliveries);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Unacknowledged).IsEmpty();
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
        await ReplayedAsync(admin, sdk, mcp, seed, first, result, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await ReplayedAsync(admin, sdk, mcp, seed, first, result, token);
        await QueueWorkerRf3Healthy.RunAsync(sdk, mcp, seed.Native, token);
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
    }

    private static async Task ReplayedAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3ByteSeed seed, Delivery first, QueueWorkerResult result, CancellationToken token)
    {
        foreach (var ack in result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack))
        {
            var receipt = await QueueWorkerRf3Completion.SuccessAsync(ack);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            await Assert.That(mutation.Id).IsNotEqualTo(QueueWorkerRf3Protocol.First);
            await QueueWorkerRf3Assertions.ReceiptAsync(receipt, ack.Command!.CommandId, seed.Native,
                [new(nameof(DeliveryAction.Ack), seed.Native.Lane.Queue, mutation.Id, QueueWorkerRf3Protocol.AckStateVersion)]);
            await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Completion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(ack, token)),
                receipt);
            await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
            await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command, token))).Value, receipt);
            await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
        }
    }
}
