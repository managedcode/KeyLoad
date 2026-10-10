using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static partial class QueueLifecyclePublicPhase
{
    internal static async Task BeginAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await BatchAsync(callers, state, [Enqueue(QueueLifecyclePublicProtocol.Parked), Enqueue(QueueLifecyclePublicProtocol.Pending)], token);
        var parked = await ClaimAsync(callers, state, QueueLifecyclePublicProtocol.Parked, token);
        await CompleteAsync(callers, state, parked, DeliveryAction.Nack, token);
        state.OriginalPending = await ClaimAsync(callers, state, QueueLifecyclePublicProtocol.Pending, token);
        await CompleteAsync(callers, state, state.OriginalPending, DeliveryAction.Nack, token);
        state.FullRefusal = new(Guid.NewGuid(), state.Partition,
            [new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Pending, QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)]);
        state.MixedRefusal = new(Guid.NewGuid(), state.Partition,
            [new CancelQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Parked, QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One),
             new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Pending, QueueLifecyclePublicProtocol.Two, QueueLifecyclePublicProtocol.One),
             new PutDocument(QueueLifecyclePublicProtocol.Collection, "refused", QueueLifecyclePublicProtocol.Payload)]);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state, state.FullRefusal, ErrorCode.ResourceExhausted, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state, state.MixedRefusal, ErrorCode.RevisionConflict, token);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(
            new(state.Partition, QueueLifecyclePublicProtocol.Collection, "refused"), token))).IsNull();
        await QueueLifecyclePublicAssertions.InitialAsync(callers, state, token);
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state, token);
    }

    private static EnqueueMessage Enqueue(string id) => new(QueueLifecyclePublicProtocol.Queue, id,
        QueueLifecyclePublicProtocol.Payload, QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);

    private static Task<CommitReceipt> BatchAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state,
        Mutation[] mutations, CancellationToken token)
        => QueueLifecyclePublicRoutes.CommitAsync(callers, state, new(Guid.NewGuid(), state.Partition, [.. mutations]), token);

    private static async Task<Delivery> ClaimAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecyclePublicProtocol.LeaseSeconds);
        var result = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Route, state.Partition, McpCallerTools.MessagesReceive,
            request, () => callers.Sdk.ReceiveAsync(request, token), token);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        await Assert.That(delivery.Token).IsNotEmpty();
        return delivery;
    }

    private static async Task CompleteAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, Delivery delivery,
        DeliveryAction action, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, action);
        var result = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Route, state.Partition, McpCallerTools.MessagesComplete,
            command, () => callers.Sdk.CompleteAsync(command, token), token);
        state.Deliveries.Add((command, result));
    }
}
