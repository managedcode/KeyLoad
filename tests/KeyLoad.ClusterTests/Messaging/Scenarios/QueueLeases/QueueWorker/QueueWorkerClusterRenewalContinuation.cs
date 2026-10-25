using System.Collections.Concurrent;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterRenewalContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, QueueWorkerDeliveryContext original, CancellationToken token)
    {
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, original, QueueWorkerClusterProtocol.FirstReadySequence, token);
        var contexts = new ConcurrentDictionary<string, QueueWorkerDeliveryContext>();
        var healthy = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (context, handlerToken) =>
        {
            handlerToken.ThrowIfCancellationRequested();
            contexts.TryAdd(context.Delivery.Id, context);
            await QueueWorkerClusterClaims.LiteralAsync(seed, context.Delivery, handlerToken);
        }, QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(healthy.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(healthy.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.RemainingReadyDeliveries);
        await Assert.That(healthy.Failures).IsEmpty();
        await Assert.That(healthy.Unacknowledged).IsEmpty();
        await Assert.That(contexts.Keys).Contains(QueueWorkerClusterProtocol.Second);
        await Assert.That(contexts.Keys).Contains(QueueWorkerClusterProtocol.Third);
        await TerminalAsync(sdk, mcp, seed, original, healthy, contexts, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await TerminalAsync(sdk, mcp, seed, original, healthy, contexts, token);
        await QueueWorkerClusterHealthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task TerminalAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerDeliveryContext original, QueueWorkerResult healthy,
        ConcurrentDictionary<string, QueueWorkerDeliveryContext> contexts, CancellationToken token)
    {
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, original, QueueWorkerClusterProtocol.FirstReadySequence, token);
        foreach (var id in new[] { QueueWorkerClusterProtocol.Second, QueueWorkerClusterProtocol.Third })
        {
            var sequence = Array.IndexOf(QueueWorkerClusterProtocol.Messages, id) + QueueWorkerClusterProtocol.ReadySequenceOffset;
            await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            var ack = await Assert.That(healthy.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
                && row.Command?.CommandId == contexts[id].AcknowledgementId)).HasSingleItem();
            var receipt = await QueueWorkerClusterCompletion.SuccessAsync(ack);
            await QueueWorkerClusterAssertions.ReceiptAsync(receipt, contexts[id].AcknowledgementId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, id, QueueWorkerClusterProtocol.AckStateVersion)]);
            await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(await sdk.ReconcileQueueWorkerOperationAsync(ack, token)),
                receipt);
            await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command!, token));
            await QueueWorkerClusterAssertions.EqualAsync(official.Value, receipt);
            await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
        }
    }
}
