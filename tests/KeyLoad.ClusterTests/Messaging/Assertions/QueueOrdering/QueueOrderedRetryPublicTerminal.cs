using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueOrderedRetryPublicTerminal
{
    internal static async Task RunAsync(RequestCqrsClusterCallers callers, QueueOrderedRetryPublicState state, bool healthy, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.First,
            new(QueueOrderedRetryPublicProtocol.First, MessageState.Acked, QueueOrderedRetryPublicProtocol.One,
                QueueOrderedRetryPublicProtocol.Ten, QueueOrderedRetryPublicProtocol.Four, null, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.Four, DeliveryGeneration: QueueOrderedRetryPublicProtocol.Two,
                EnqueueSequence: QueueOrderedRetryPublicProtocol.One), false, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.Second,
            new(QueueOrderedRetryPublicProtocol.Second, MessageState.Acked, QueueOrderedRetryPublicProtocol.One,
                QueueOrderedRetryPublicProtocol.Three, QueueOrderedRetryPublicProtocol.Two, null, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.One, EnqueueSequence: QueueOrderedRetryPublicProtocol.Two), false, token);
        if (healthy)
        {
            await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.Healthy,
                new(QueueOrderedRetryPublicProtocol.Healthy, MessageState.Acked, QueueOrderedRetryPublicProtocol.One,
                    QueueOrderedRetryPublicProtocol.Three, QueueOrderedRetryPublicProtocol.Five, null, null,
                    LeaseVersion: QueueOrderedRetryPublicProtocol.One, EnqueueSequence: state.HealthyEnqueue), false, token);
        }
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var request = new ReceiveRequest(Guid.NewGuid(), state.Caller.Lane, MaxMessages: QueueOrderedRetryPublicProtocol.One);
            var received = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Caller.Partition, McpCallerTools.MessagesReceive,
                request, () => callers.Sdk.ReceiveAsync(request, token), token);
            await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
            await Assert.That(received.Deliveries).IsEmpty();
        }
    }
}
