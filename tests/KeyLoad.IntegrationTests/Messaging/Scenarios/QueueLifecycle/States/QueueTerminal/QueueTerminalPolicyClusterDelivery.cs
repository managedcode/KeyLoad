using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyClusterDelivery
{
    internal static async Task<Delivery> ClaimAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, int attempt, bool healthy, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueTerminalPolicyClusterProtocol.LeaseSeconds);
        var received = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(received.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var id = healthy ? QueueTerminalPolicyClusterProtocol.Healthy : QueueTerminalPolicyClusterProtocol.Message;
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueTerminalPolicyClusterProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueTerminalPolicyClusterProtocol.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(attempt);
        await Assert.That(delivery.LeaseVersion).IsEqualTo((long)attempt);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Token).IsNotEmpty();
        var metadata = new MessageMetadata(id, MessageState.Leased, attempt, healthy ? 2L : 3L * attempt - 1L,
            healthy ? 4L : attempt, null, null, state.Identity.Principal.Id, attempt, delivery.LeaseUntil,
            SafeFailureCode: !healthy && attempt > 1 ? QueueTerminalPolicyClusterProtocol.Retry : null);
        Set(state, new(metadata, delivery.PayloadJson, delivery.HeadersJson), healthy);
        state.Claims.Add((request, received));
        await QueueTerminalPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterReplay.ClaimAsync(worker, request, received,
            () => QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
        return delivery;
    }

    internal static async Task CompleteAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, Delivery delivery, int attempt, bool healthy, CancellationToken token)
    {
        var action = healthy ? DeliveryAction.Ack : DeliveryAction.Nack;
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, action);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(request, token));
        var version = healthy ? 3L : 3L * attempt;
        await QueueTerminalPolicyClusterAssertions.ReceiptAsync(receipt, request.CommandId, state,
            [new(action.ToString(), state.Lane.Queue, delivery.Id, version)]);
        DateTimeOffset? due = null;
        if (!healthy && attempt < QueueTerminalPolicyClusterProtocol.Attempts)
        {
            var recorded = await PublicReadAssertions.MessageAsync(await worker.Sdk.InspectAsync(new(state.Lane, delivery.Id), token));
            await Assert.That(recorded).IsNotNull();
            due = recorded!.Metadata.NotBefore;
            await Assert.That(due).IsNotNull();
        }
        var terminal = attempt == QueueTerminalPolicyClusterProtocol.Attempts;
        var metadata = new MessageMetadata(delivery.Id, healthy ? MessageState.Acked : terminal
            ? MessageState.DeadLettered : MessageState.Scheduled, attempt, version, healthy ? 4L : attempt,
            due, null, LeaseVersion: attempt, SafeFailureCode: healthy ? null : terminal
                ? QueueTerminalPolicyClusterProtocol.Exhausted : QueueTerminalPolicyClusterProtocol.Retry,
            ParkedSequence: terminal && !healthy ? 1L : 0L);
        Set(state, new(metadata, healthy ? null : QueueTerminalPolicyClusterProtocol.Payload,
            healthy ? null : QueueTerminalPolicyClusterProtocol.Headers), healthy);
        state.Completions.Add((request, receipt));
        await QueueTerminalPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterReplay.CompletionAsync(worker, request, receipt,
            () => QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }

    private static void Set(QueueTerminalPolicyClusterState state, MessageInspection message, bool healthy)
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
