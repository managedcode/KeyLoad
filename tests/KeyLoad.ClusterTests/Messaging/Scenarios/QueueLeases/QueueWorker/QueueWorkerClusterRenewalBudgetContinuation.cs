using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterRenewalBudgetContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, QueueWorkerOperationObservation original, MessageInspection leased, CancellationToken token)
    {
        await ReplayedAsync(sdk, mcp, seed, original, leased, token);
        var fresh = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(),
            (context, handlerToken) => QueueWorkerClusterClaims.LiteralAsync(seed, context.Delivery, handlerToken),
            QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.RemainingReadyDeliveries), TimeProvider.System, token);
        await Assert.That(fresh.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.RemainingReadyDeliveries);
        await Assert.That(fresh.Reason).IsEqualTo(QueueWorkerStopReason.DeliveryBudget);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Second, QueueWorkerClusterProtocol.SecondReadySequence, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Third, QueueWorkerClusterProtocol.ThirdReadySequence, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await ReplayedAsync(sdk, mcp, seed, original, leased, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Second, QueueWorkerClusterProtocol.SecondReadySequence, token);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Third, QueueWorkerClusterProtocol.ThirdReadySequence, token);
        await QueueWorkerClusterHealthy.RunAsync(sdk, mcp, seed, token);
    }

    private static async Task ReplayedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerOperationObservation original, MessageInspection leased, CancellationToken token)
    {
        await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerClusterProtocol.First, token), leased);
        var receipt = await QueueWorkerClusterCompletion.SuccessAsync(original);
        await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterCompletion.SuccessAsync(
            await sdk.ReconcileQueueWorkerOperationAsync(original, token)), receipt);
        await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerClusterProtocol.First, token), leased);
        await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, original.Command!, token))).Value, receipt);
        await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Lane,
            QueueWorkerClusterProtocol.First, token), leased);
    }
}
