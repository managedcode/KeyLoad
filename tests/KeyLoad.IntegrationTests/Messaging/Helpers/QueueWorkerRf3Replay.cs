using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Replay
{
    internal static async Task AcksAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerRf3HandlerLifecycle handlers, QueueWorkerOperationObservation[] originals, CancellationToken token)
    {
        foreach (var original in originals)
        {
            var command = original.Command!;
            var receipt = await QueueWorkerRf3Completion.SuccessAsync(original);
            var context = handlers.Contexts.Values.Single(row => row.AcknowledgementId == command.CommandId);
            await QueueWorkerRf3Assertions.ReceiptAsync(receipt, command.CommandId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, context.Delivery.Id, QueueWorkerRf3Protocol.AckStateVersion)]);
            var sdkReplay = await sdk.ReconcileQueueWorkerOperationAsync(original, token);
            await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Completion.SuccessAsync(sdkReplay), receipt);
            await QueueWorkerRf3Continuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
            var mcpReplay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, command, token));
            await QueueWorkerRf3Assertions.EqualAsync(mcpReplay.Value, receipt);
            await QueueWorkerRf3Continuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
        }
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(seed.Producer, token)), seed.Receipt);
        await QueueWorkerRf3Continuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
    }
}
