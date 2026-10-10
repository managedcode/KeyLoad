using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Queue
{
    internal static async Task ContinueAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var queue = state.CurrentQueue;
        await QueueLifecyclePublicAssertions.InitialAsync(callers, queue, token);
        queue.FullRefusal = state.OriginalQueue.FullRefusal! with { CommandId = Guid.NewGuid() };
        queue.MixedRefusal = state.OriginalQueue.MixedRefusal! with { CommandId = Guid.NewGuid() };
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, queue, queue.FullRefusal, ErrorCode.ResourceExhausted, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, queue, queue.MixedRefusal, ErrorCode.RevisionConflict, token);
        await QueueLifecyclePublicAssertions.InitialAsync(callers, queue, token);
        await ClusterRestoreMixedRetentionRf3Literal.AbsentDocumentAsync(callers, state,
            QueueLifecyclePublicProtocol.Collection, "refused", token);
        _ = await BatchAsync(callers, queue, [new CancelQueueMessage(queue.Lane.Queue, QueueLifecyclePublicProtocol.Parked,
            QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)], token);
        _ = await BatchAsync(callers, queue, [new ParkPendingQueueMessage(queue.Lane.Queue, QueueLifecyclePublicProtocol.Pending,
            QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)], token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, queue, QueueLifecyclePublicProtocol.Pending,
            new(QueueLifecyclePublicProtocol.Pending, MessageState.DeadLettered, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.Two, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.One, SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueLifecyclePublicProtocol.Two), true, token);
        _ = await BatchAsync(callers, queue, [new RedriveQueueMessage(queue.Lane.Queue, QueueLifecyclePublicProtocol.Pending,
            QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.One)], token);
        var delivery = await ClaimAsync(callers, queue, QueueLifecyclePublicProtocol.Pending, token);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueLifecyclePublicProtocol.Two);
        _ = await CompleteAsync(callers, queue, delivery, token);
        await QueueLifecyclePublicAssertions.TerminalAsync(callers, queue, token);
        _ = await BatchAsync(callers, queue, [new EnqueueMessage(queue.Lane.Queue, QueueLifecyclePublicProtocol.Healthy,
            QueueLifecyclePublicProtocol.Payload, QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering)], token);
        _ = await CompleteAsync(callers, queue, await ClaimAsync(callers, queue, QueueLifecyclePublicProtocol.Healthy, token), token);
        await HealthyAsync(callers, state, token);
        await ReplayAsync(callers, state, token);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.TerminalAsync(callers, state.CurrentQueue, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.CurrentQueue, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Acked, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Four, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.One), false, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.CurrentQueue, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.CurrentQueue, state.CurrentQueue.FullRefusal!, ErrorCode.ResourceExhausted, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.CurrentQueue, state.CurrentQueue.MixedRefusal!, ErrorCode.RevisionConflict, token);
    }

    private static Task<CommitReceipt> BatchAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state,
        Mutation[] mutations, CancellationToken token)
        => QueueLifecyclePublicRoutes.CommitAsync(callers, state, new(Guid.NewGuid(), state.Partition, [.. mutations]), token);

    private static async Task<Delivery> ClaimAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecyclePublicProtocol.LeaseSeconds);
        var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Route, state.Partition, McpCallerTools.MessagesReceive,
            request, () => callers.Sdk.ReceiveAsync(request, token), token);
        await Assert.That(actual.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        await Assert.That(delivery.Token).IsNotEmpty();
        return delivery;
    }

    private static async Task<CommitReceipt> CompleteAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state,
        Delivery delivery, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Route, state.Partition, McpCallerTools.MessagesComplete,
            command, () => callers.Sdk.CompleteAsync(command, token), token);
        state.Deliveries.Add((command, receipt));
        return receipt;
    }
}
