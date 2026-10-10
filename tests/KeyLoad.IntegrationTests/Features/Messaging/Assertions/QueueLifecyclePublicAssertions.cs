using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicAssertions
{
    internal static async Task LiteralAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, string id,
        MessageMetadata metadata, bool retained, CancellationToken token)
    {
        var expected = new MessageInspection(metadata, retained ? QueueLifecyclePublicProtocol.Payload : null,
            retained ? QueueLifecyclePublicProtocol.Headers : null);
        var request = new InspectMessageRequest(state.Lane, id);
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.MessagesInspect,
                request, () => callers.Sdk.InspectAsync(request, token), token);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }
    }

    internal static async Task InitialAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Parked,
            new(QueueLifecyclePublicProtocol.Parked, MessageState.DeadLettered, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.One, SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueLifecyclePublicProtocol.One), true, token);
        await LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Pending,
            new(QueueLifecyclePublicProtocol.Pending, MessageState.PendingDeadLetter, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Two, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.One, SafeFailureCode: "AttemptsExhausted"), true, token);
    }

    internal static async Task TerminalAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Parked,
            new(QueueLifecyclePublicProtocol.Parked, MessageState.Cancelled, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.One, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.Two, DeliveryGeneration: QueueLifecyclePublicProtocol.Two, SafeFailureCode: "AttemptsExhausted"), false, token);
        await LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Pending,
            new(QueueLifecyclePublicProtocol.Pending, MessageState.Acked, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Seven, QueueLifecyclePublicProtocol.Three, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.Three, DeliveryGeneration: QueueLifecyclePublicProtocol.Two), false, token);
    }
}
