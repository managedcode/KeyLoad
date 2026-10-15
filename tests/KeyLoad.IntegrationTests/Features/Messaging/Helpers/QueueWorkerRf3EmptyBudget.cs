using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3EmptyBudget
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var options = QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery).Value with
        {
            MaximumReceiveCalls = QueueWorkerRf3Protocol.SingleReceiveBudget
        };
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, id, RejectHandlerAsync, Options.Create(options),
            TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.ReceiveBudget);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerRf3Protocol.NoDeliveries);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerRf3Protocol.NoActiveHandlers);
        await Assert.That(result.PeakInFlightBytes).IsEqualTo((long)QueueWorkerRf3Protocol.NoActiveBytes);
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Operations.Any(row => row.Command is not null)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned)).HasSingleItem();
        await Assert.That(original.Receive!.RequestId).IsEqualTo(id);
        await Assert.That(original.Claims).IsEmpty();
        await Assert.That(original.ReceiveFailure).IsNull();
        var cut = original.ReceiveToken!;
        await Assert.That(cut.Incarnation).IsEqualTo(seed.Placement.Incarnation);
        await Assert.That(cut.OwnershipEpoch).IsEqualTo(seed.Placement.PlacementEpoch);
        await Assert.That(cut.AtomicPartitionId).IsEqualTo(seed.Lane.Partition.AtomicPartitionId);
        await Assert.That(cut.Position).IsGreaterThan((long)QueueWorkerRf3Protocol.PositivePositionBoundary);
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveAsync(original.Receive, token)), new ReceiveResult(id, [], cut));
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Receive, token))).Value, new ReceiveResult(id, [], cut));
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerRf3Protocol.Missing);
}
