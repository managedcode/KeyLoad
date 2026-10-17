using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static partial class QueueLifecyclePublicPhase
{
    internal static async Task ContinueAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.InitialAsync(callers, state, token);
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state, state.FullRefusal!, ErrorCode.ResourceExhausted, token);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state, state.MixedRefusal!, ErrorCode.RevisionConflict, token);
        await BatchAsync(callers, state, [new CancelQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Parked,
            QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)], token);
        await SqlPendingQueuePublicFlow.RunAsync(fixture, callers, state, token);
        await BatchAsync(callers, state, [new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Pending,
            QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)], token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Pending,
            new(QueueLifecyclePublicProtocol.Pending, MessageState.DeadLettered, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.Two, null, null,
                LeaseVersion: QueueLifecyclePublicProtocol.One, SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueLifecyclePublicProtocol.Two), true, token);
        await BatchAsync(callers, state, [new RedriveQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Pending,
            QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.One)], token);
        var delivery = await ClaimAsync(callers, state, QueueLifecyclePublicProtocol.Pending, token);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueLifecyclePublicProtocol.Two);
        var old = new DeliveryCommand(Guid.NewGuid(), state.Lane, state.OriginalPending!.Token, DeliveryAction.Ack);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.CompleteAsync(old, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, old, token), ErrorCode.StaleLease, dispatched: true);
        await CompleteAsync(callers, state, delivery, DeliveryAction.Ack, token);
        await QueueLifecyclePublicAssertions.TerminalAsync(callers, state, token);
        await BatchAsync(callers, state, [Enqueue(QueueLifecyclePublicProtocol.Healthy)], token);
        var healthy = await ClaimAsync(callers, state, QueueLifecyclePublicProtocol.Healthy, token);
        await CompleteAsync(callers, state, healthy, DeliveryAction.Ack, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Acked, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Four, null, null, LeaseVersion: QueueLifecyclePublicProtocol.One), false, token);
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state, token);
    }
}
