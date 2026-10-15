using System.Collections.Concurrent;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3RenewalContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, QueueWorkerDeliveryContext original, CancellationToken token)
    {
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, original, QueueWorkerRf3Protocol.FirstReadySequence, token);
        var contexts = new ConcurrentDictionary<string, QueueWorkerDeliveryContext>();
        var healthy = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (context, handlerToken) =>
        {
            handlerToken.ThrowIfCancellationRequested();
            contexts.TryAdd(context.Delivery.Id, context);
            await QueueWorkerRf3Claims.LiteralAsync(seed, context.Delivery, handlerToken);
        }, QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(healthy.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(healthy.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.RemainingReadyDeliveries);
        await Assert.That(healthy.Failures).IsEmpty();
        await Assert.That(healthy.Unacknowledged).IsEmpty();
        await Assert.That(contexts.Keys).Contains(QueueWorkerRf3Protocol.Second);
        await Assert.That(contexts.Keys).Contains(QueueWorkerRf3Protocol.Third);
        await TerminalAsync(sdk, mcp, seed, original, healthy, contexts, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await TerminalAsync(sdk, mcp, seed, original, healthy, contexts, token);
        await QueueWorkerRf3Healthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task TerminalAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerDeliveryContext original, QueueWorkerResult healthy,
        ConcurrentDictionary<string, QueueWorkerDeliveryContext> contexts, CancellationToken token)
    {
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, original, QueueWorkerRf3Protocol.FirstReadySequence, token);
        foreach (var id in new[] { QueueWorkerRf3Protocol.Second, QueueWorkerRf3Protocol.Third })
        {
            var sequence = Array.IndexOf(QueueWorkerRf3Protocol.Messages, id) + QueueWorkerRf3Protocol.ReadySequenceOffset;
            await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            var ack = await Assert.That(healthy.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
                && row.Command?.CommandId == contexts[id].AcknowledgementId)).HasSingleItem();
            await QueueWorkerRf3Assertions.ReceiptAsync(ack.Completion!.Value!, contexts[id].AcknowledgementId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, id, QueueWorkerRf3Protocol.AckStateVersion)]);
            await QueueWorkerRf3Assertions.EqualAsync((await sdk.ReconcileQueueWorkerOperationAsync(ack, token)).Completion!.Value,
                ack.Completion.Value);
            await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack.Command!, token));
            await QueueWorkerRf3Assertions.EqualAsync(official.Value, ack.Completion.Value);
            await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
        }
    }
}
