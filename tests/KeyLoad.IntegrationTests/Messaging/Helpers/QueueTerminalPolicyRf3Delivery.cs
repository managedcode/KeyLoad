using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Delivery
{
    internal static async Task<Delivery> ClaimAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, int attempt, bool healthy, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueTerminalPolicyRf3Protocol.LeaseSeconds);
        var received = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(received.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(received.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(received.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var id = healthy ? QueueTerminalPolicyRf3Protocol.Healthy : QueueTerminalPolicyRf3Protocol.Message;
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueTerminalPolicyRf3Protocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueTerminalPolicyRf3Protocol.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(attempt);
        await Assert.That(delivery.LeaseVersion).IsEqualTo((long)attempt);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Token).IsNotEmpty();
        var metadata = new MessageMetadata(id, MessageState.Leased, attempt, healthy ? 2L : 3L * attempt - 1L,
            healthy ? 4L : attempt, null, null, state.Identity.Principal.Id, attempt, delivery.LeaseUntil,
            SafeFailureCode: !healthy && attempt > 1 ? QueueTerminalPolicyRf3Protocol.Retry : null);
        Set(state, new(metadata, delivery.PayloadJson, delivery.HeadersJson), healthy);
        state.Claims.Add((request, received));
        await QueueTerminalPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Replay.ClaimAsync(worker, request, received,
            () => QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
        return delivery;
    }

    internal static async Task CompleteAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, Delivery delivery, int attempt, bool healthy, CancellationToken token)
    {
        var action = healthy ? DeliveryAction.Ack : DeliveryAction.Nack;
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, action);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(request, token));
        var version = healthy ? 3L : 3L * attempt;
        await QueueTerminalPolicyRf3Assertions.ReceiptAsync(receipt, request.CommandId, state,
            [new(action.ToString(), state.Lane.Queue, delivery.Id, version)]);
        DateTimeOffset? due = null;
        if (!healthy && attempt < QueueTerminalPolicyRf3Protocol.Attempts)
        {
            var recorded = await PublicReadAssertions.MessageAsync(await worker.Sdk.InspectAsync(new(state.Lane, delivery.Id), token));
            await Assert.That(recorded).IsNotNull();
            due = recorded!.Metadata.NotBefore;
            await Assert.That(due).IsNotNull();
        }
        var terminal = attempt == QueueTerminalPolicyRf3Protocol.Attempts;
        var metadata = new MessageMetadata(delivery.Id, healthy ? MessageState.Acked : terminal
            ? MessageState.DeadLettered : MessageState.Scheduled, attempt, version, healthy ? 4L : attempt,
            due, null, LeaseVersion: attempt, SafeFailureCode: healthy ? null : terminal
                ? QueueTerminalPolicyRf3Protocol.Exhausted : QueueTerminalPolicyRf3Protocol.Retry,
            ParkedSequence: terminal && !healthy ? 1L : 0L);
        Set(state, new(metadata, healthy ? null : QueueTerminalPolicyRf3Protocol.Payload,
            healthy ? null : QueueTerminalPolicyRf3Protocol.Headers), healthy);
        state.Completions.Add((request, receipt));
        await QueueTerminalPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Replay.CompletionAsync(worker, request, receipt,
            () => QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }

    private static void Set(QueueTerminalPolicyRf3State state, MessageInspection message, bool healthy)
    {
        if (healthy)
        {
            state.Healthy = message;
        }
        else
        {
            state.Original = message;
        }
    }
}
