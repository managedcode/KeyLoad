using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueParkedPolicyClusterDelivery
{
    internal static async Task EmptyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var result = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries).IsEmpty();
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.ClaimAsync(worker, request, result,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task CancelAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, string id, CancellationToken token)
    {
        var previous = state.Messages[id]!.Metadata;
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new CancelQueueMessage(state.Lane.Queue, id, 1L, 1L)]);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await worker.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        await QueueParkedPolicyClusterAssertions.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueParkedPolicyClusterProtocol.Cancel, state.Lane.Queue, id, 2L)]);
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
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.CommandAsync(worker, command, receipt,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task<Delivery> ClaimAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueParkedPolicyClusterProtocol.LeaseSeconds);
        var result = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueParkedPolicyClusterProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueParkedPolicyClusterProtocol.Headers);
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
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.ClaimAsync(worker, request, result,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
        return delivery;
    }

    internal static async Task AckAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, Delivery delivery, CancellationToken token)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(request, token));
        await QueueParkedPolicyClusterAssertions.ReceiptAsync(receipt, request.CommandId, state,
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
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.CompletionAsync(worker, request, receipt,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }
}
