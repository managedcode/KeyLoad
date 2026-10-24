using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterReplay
{
    internal static async Task AcksAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerClusterHandlerLifecycle handlers, QueueWorkerOperationObservation[] originals, CancellationToken token)
    {
        foreach (var original in originals)
        {
            var command = original.Command!;
            var receipt = await QueueWorkerClusterCompletion.SuccessAsync(original);
            var context = handlers.Contexts.Values.Single(row => row.AcknowledgementId == command.CommandId);
            await QueueWorkerClusterAssertions.ReceiptAsync(receipt, command.CommandId, seed,
                [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, context.Delivery.Id, QueueWorkerClusterProtocol.AckStateVersion)]);
            var sdkReplay = await sdk.ReconcileQueueWorkerOperationAsync(original, token);
            await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(sdkReplay), receipt);
            await QueueWorkerClusterContinuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
            var mcpReplay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, command, token));
            await QueueWorkerClusterAssertions.EqualAsync(mcpReplay.Value, receipt);
            await QueueWorkerClusterContinuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
        }
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(seed.Producer, token)), seed.Receipt);
        await QueueWorkerClusterContinuation.ImagesAsync(sdk, mcp, seed, handlers, originals, token);
    }
}
