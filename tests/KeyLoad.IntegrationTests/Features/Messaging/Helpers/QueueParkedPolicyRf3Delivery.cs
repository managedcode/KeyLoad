using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyRf3Delivery
{
    internal static async Task EmptyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var result = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries).IsEmpty();
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.ClaimAsync(worker, request, result,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task CancelAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, string id, CancellationToken token)
    {
        var previous = state.Messages[id]!.Metadata;
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new CancelQueueMessage(state.Lane.Queue, id, 1L, 1L)]);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await worker.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        await QueueParkedPolicyRf3Assertions.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueParkedPolicyRf3Protocol.Cancel, state.Lane.Queue, id, 2L)]);
        state.Commands.Add((command, receipt));
        state.Messages[id] = new(previous with
        {
            State = MessageState.Cancelled,
            StateVersion = 2L,
            DeliveryGeneration = 2L,
            LeaseVersion = 1L,
            NotBefore = null,
            ActiveOrderSequence = 0L
        }, null, null);
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.CommandAsync(worker, command, receipt,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task<Delivery> ClaimAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueParkedPolicyRf3Protocol.LeaseSeconds);
        var result = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueParkedPolicyRf3Protocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueParkedPolicyRf3Protocol.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Token).IsNotEmpty();
        state.Messages[id] = state.Messages[id]! with
        {
            Metadata = state.Messages[id]!.Metadata with
            {
                State = MessageState.Leased,
                Attempts = 1,
                StateVersion = 2L,
                LeaseOwner = state.Identity.Principal.Id,
                LeaseVersion = 1L,
                LeaseUntil = delivery.LeaseUntil
            }
        };
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.ClaimAsync(worker, request, result,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
        return delivery;
    }

    internal static async Task AckAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, Delivery delivery, CancellationToken token)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(request, token));
        await QueueParkedPolicyRf3Assertions.ReceiptAsync(receipt, request.CommandId, state,
            [new(DeliveryAction.Ack.ToString(), state.Lane.Queue, delivery.Id, 3L)]);
        state.Completions.Add((request, receipt));
        state.Messages[delivery.Id] = new(state.Messages[delivery.Id]!.Metadata with
        {
            State = MessageState.Acked,
            StateVersion = 3L,
            LeaseOwner = null,
            LeaseUntil = null,
            ActiveOrderSequence = 0L
        }, null, null);
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.CompletionAsync(worker, request, receipt,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }
}
