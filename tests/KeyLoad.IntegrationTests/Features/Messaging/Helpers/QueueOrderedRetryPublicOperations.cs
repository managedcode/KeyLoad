using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicOperations
{
    internal static EnqueueMessage Literal(string id) => new(QueueLifecyclePublicProtocol.Queue, id,
        QueueLifecyclePublicProtocol.Payload, QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);
    internal static async Task<ReceiveResult> ReceiveAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Caller.Lane, MaxMessages: QueueOrderedRetryPublicProtocol.One);
        return await QueueLifecyclePublicRoutes.CallAsync(callers, state.Caller.Route, state.Caller.Partition, McpCallerTools.MessagesReceive,
            request, () => callers.Sdk.ReceiveAsync(request, token), token);
    }
    internal static async Task<Delivery> ClaimAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, string id, CancellationToken token)
    {
        var delivery = await Assert.That((await ReceiveAsync(callers, state, token)).Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        return delivery;
    }
    internal static async Task CompleteAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, Delivery lease,
        DeliveryAction action, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Caller.Lane, lease.Token, action);
        var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Caller.Route, state.Caller.Partition, McpCallerTools.MessagesComplete,
            command, () => callers.Sdk.CompleteAsync(command, token), token);
        state.Caller.Deliveries.Add((command, receipt));
    }
}
