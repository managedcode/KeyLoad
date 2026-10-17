using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3RenewalBudgetContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, QueueWorkerOperationObservation original, MessageInspection leased, CancellationToken token)
    {
        await ReplayedAsync(sdk, mcp, seed, original, leased, token);
        var fresh = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerRf3Claims.LiteralAsync(seed, context.Delivery, handlerToken),
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(fresh.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.RemainingReadyDeliveries);
        await Assert.That(fresh.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Second, QueueWorkerRf3Protocol.SecondReadySequence, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Third, QueueWorkerRf3Protocol.ThirdReadySequence, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await ReplayedAsync(sdk, mcp, seed, original, leased, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Second, QueueWorkerRf3Protocol.SecondReadySequence, token);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Third, QueueWorkerRf3Protocol.ThirdReadySequence, token);
        await QueueWorkerRf3Healthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task ReplayedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerOperationObservation original, MessageInspection leased, CancellationToken token)
    {
        await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerRf3Protocol.First, token), leased);
        var receipt = await QueueWorkerRf3Completion.SuccessAsync(original);
        await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Completion.SuccessAsync(
            await sdk.ReconcileQueueWorkerOperationAsync(original, token)), receipt);
        await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerRf3Protocol.First, token), leased);
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, original.Command!, token))).Value, receipt);
        await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerRf3Protocol.First, token), leased);
    }
}
