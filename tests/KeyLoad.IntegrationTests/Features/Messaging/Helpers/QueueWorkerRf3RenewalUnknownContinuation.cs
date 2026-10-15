using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3RenewalUnknownContinuation
{
    internal static async Task LeasedAsync(MessageInspection actual, QueueWorkerDeliveryContext context, string principal)
    {
        await Assert.That(actual.Metadata.LeaseUntil!.Value).IsGreaterThanOrEqualTo(context.Delivery.LeaseUntil);
        await QueueWorkerRf3Assertions.EqualAsync(actual,
            new MessageInspection(new(MultiLaneReceiveRf3Flow.Message, MessageState.Leased,
                QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.RenewedStateVersion,
                QueueWorkerRf3Protocol.FirstReadySequence, null, null, principal, QueueWorkerRf3Protocol.InitialLeaseVersion,
                actual.Metadata.LeaseUntil, QueueWorkerRf3Protocol.InitialDeliveryGeneration),
                MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers));
    }

    internal static async Task ReconcileAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest requests, QueueWorkerOperationObservation original, DeliveryCommand parentAck,
        MessageInspection leased, MessageInspection ready, CancellationToken token)
    {
        var recovered = await caller.Sdk.ReconcileQueueWorkerOperationAsync(original, token);
        var receipt = await QueueWorkerRf3Completion.SuccessAsync(recovered);
        await ReceiptAsync(administrator.Sdk, receipt, original.Command!, QueueWorkerRf3Protocol.RenewedStateVersion, token);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, requests, leased, ready, token);
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, original.Command!, token))).Value, receipt);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, requests, leased, ready, token);
        // Explicit parent ACK follows a cancelled read-only callback; the worker admitted no ACK.
        var acknowledged = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(parentAck, token));
        await ReceiptAsync(administrator.Sdk, acknowledged, parentAck, QueueWorkerRf3Protocol.RenewedAckStateVersion, token);
        var terminal = leased with
        {
            Metadata = leased.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = QueueWorkerRf3Protocol.RenewedAckStateVersion,
                LeaseOwner = null,
                LeaseUntil = null
            },
            PayloadJson = null,
            HeadersJson = null
        };
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, requests, terminal, ready, token);
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, parentAck, token))).Value, acknowledged);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, requests, terminal, ready, token);
    }

    private static async Task ReceiptAsync(KeyLoadClient administrator, CommitReceipt receipt, DeliveryCommand command,
        long revision, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(QueueWorkerRf3Protocol.PlacementSchemaVersion, command.Lane.Partition), token));
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)QueueWorkerRf3Protocol.PositivePositionBoundary);
        await QueueWorkerRf3Assertions.EqualAsync(receipt, new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Lane.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new(command.Action.ToString(), command.Lane.Queue, MultiLaneReceiveRf3Flow.Message, revision)], DurabilityProfile.QuorumProcessDurable));
    }
}
