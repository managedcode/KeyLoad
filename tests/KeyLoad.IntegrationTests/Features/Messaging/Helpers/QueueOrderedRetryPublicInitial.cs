using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicInitial
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Caller, new(Guid.NewGuid(), state.Caller.Partition,
            [QueueOrderedRetryPublicOperations.Literal(QueueOrderedRetryPublicProtocol.First),
             QueueOrderedRetryPublicOperations.Literal(QueueOrderedRetryPublicProtocol.Second)]), token);
        state.Original = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.First, token);
        await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, state.Original, DeliveryAction.Renew, token);
        await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, state.Original, DeliveryAction.Nack, token);
        var request = new InspectMessageRequest(state.Caller.Lane, QueueOrderedRetryPublicProtocol.First);
        var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Caller.Route, state.Caller.Partition, McpCallerTools.MessagesInspect,
            request, () => callers.Sdk.InspectAsync(request, token), token);
        var metadata = actual?.Metadata ?? throw new InvalidOperationException("The recorded retry metadata is missing.");
        var retryAt = metadata.NotBefore ?? throw new InvalidOperationException("The recorded retry deadline is missing.");
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.First,
            new(QueueOrderedRetryPublicProtocol.First, MessageState.Scheduled, QueueOrderedRetryPublicProtocol.One,
                QueueOrderedRetryPublicProtocol.Four, QueueOrderedRetryPublicProtocol.One, retryAt, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.One, SafeFailureCode: "RetryRequested"), true, token);
        await DueRecurringRf3Assertions.WaitForDueTimeAsync(retryAt, token);
        var second = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.First, token);
        await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, second, DeliveryAction.Nack, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.First,
            new(QueueOrderedRetryPublicProtocol.First, MessageState.DeadLettered, QueueOrderedRetryPublicProtocol.Two,
                QueueOrderedRetryPublicProtocol.Seven, QueueOrderedRetryPublicProtocol.Three, null, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.Two, SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueOrderedRetryPublicProtocol.One), true, token);
        if (state.ParkedHead == QueueParkedHeadPolicy.Continue)
        { state.Second = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.Second, token); }
        else
        { await Assert.That((await QueueOrderedRetryPublicOperations.ReceiveAsync(callers, state, token)).Deliveries).IsEmpty(); }
    }
}
