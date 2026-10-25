using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterByteContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient admin, KeyLoadClient sdk,
        McpOfficialClient mcp, QueueWorkerClusterByteSeed seed, Delivery first, CancellationToken token)
    {
        var result = await sdk.RunQueueAckWorkerAsync(seed.Execution.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerClusterByteImages.DeliveryAsync(seed, context.Delivery, handlerToken),
            QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.RemainingReadyDeliveries);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Unacknowledged).IsEmpty();
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
        await ReplayedAsync(admin, sdk, mcp, seed, first, result, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await ReplayedAsync(admin, sdk, mcp, seed, first, result, token);
        await QueueWorkerClusterHealthy.RunAsync(sdk, mcp, seed.Execution, token);
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
    }

    private static async Task ReplayedAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterByteSeed seed, Delivery first, QueueWorkerResult result, CancellationToken token)
    {
        foreach (var ack in result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack))
        {
            var receipt = await QueueWorkerClusterCompletion.SuccessAsync(ack);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            await Assert.That(mutation.Id).IsNotEqualTo(QueueWorkerClusterProtocol.First);
            await QueueWorkerClusterAssertions.ReceiptAsync(receipt, ack.Command!.CommandId, seed.Execution,
                [new(nameof(DeliveryAction.Ack), seed.Execution.Lane.Queue, mutation.Id, QueueWorkerClusterProtocol.AckStateVersion)]);
            await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(ack, token)),
                receipt);
            await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
            await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command, token))).Value, receipt);
            await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, first, true, token);
        }
    }
}
