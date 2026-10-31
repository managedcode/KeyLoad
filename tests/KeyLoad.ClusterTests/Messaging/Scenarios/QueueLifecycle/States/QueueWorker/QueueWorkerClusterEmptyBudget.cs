using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterEmptyBudget
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var options = QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery).Value with
        {
            MaximumReceiveCalls = QueueWorkerClusterProtocol.SingleReceiveBudget
        };
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, id, RejectHandlerAsync, Options.Create(options),
            TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.ReceiveBudget);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerClusterProtocol.NoDeliveries);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.NoAcknowledgements);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerClusterProtocol.NoActiveHandlers);
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Operations.Any(row => row.Command is not null)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned)).HasSingleItem();
        await Assert.That(original.Receive!.RequestId).IsEqualTo(id);
        await Assert.That(original.Claims).IsEmpty();
        await Assert.That(original.ReceiveFailure).IsNull();
        var cut = original.ReceiveToken!;
        await Assert.That(cut.AtomicPartitionId).IsEqualTo(seed.Lane.Partition.AtomicPartitionId);
        await Assert.That(cut.Position).IsGreaterThan((long)QueueWorkerClusterProtocol.PositivePositionBoundary);
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveAsync(original.Receive, token)), new ReceiveResult(id, [], cut));
        await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Receive, token))).Value, new ReceiveResult(id, [], cut));
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
}
