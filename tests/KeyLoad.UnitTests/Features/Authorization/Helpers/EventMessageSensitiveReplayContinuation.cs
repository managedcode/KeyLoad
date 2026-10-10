using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveReplayContinuation
{
    internal static async Task RepairAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        EventMessageSensitiveReplayOperations.Principal(database, state.Worker with
        { Revoked = true, PolicyEpoch = EventMessageSensitiveReplayProtocol.RevokedEpoch }, token);
        var id = Guid.NewGuid();
        var refusal = EventMessageSensitiveReplayOperations.Apply(database, state.ReceiveKind,
            EventMessageSensitiveReplayOperations.Receive(state, id, true), state.Caller, id, token);
        await EventMessageSensitiveReplayAssertions.DeniedAsync(refusal, ErrorCode.Unauthenticated);
        EventMessageSensitiveReplayOperations.Principal(database, state.Worker with
        { PolicyEpoch = EventMessageSensitiveReplayProtocol.RepairedEpoch, FieldGrants = [EventMessageSensitiveReplayProtocol.GrantAfter] }, token);
    }

    internal static async Task HealthyAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        if (!state.Subscription)
        {
            var id = Guid.NewGuid();
            var original = state.Original.Get<ReceiveResult>().Deliveries.Single();
            EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Delivery,
                new DeliveryCommand(id, state.Lane, original.Token, DeliveryAction.Ack), state.Caller, id, token).Get<CommitReceipt>();
        }
        state.HealthyId = Guid.NewGuid();
        state.HealthyRequest = EventMessageSensitiveReplayOperations.Receive(state, state.HealthyId, true);
        state.Healthy = EventMessageSensitiveReplayOperations.Apply(database, state.ReceiveKind,
            state.HealthyRequest, state.Caller, state.HealthyId, token);
        await EventMessageSensitiveReplayAssertions.FullAsync(state.Healthy, state);
        await EventMessageSensitiveReplayAssertions.HealthyAsync(database, state, token);
    }

    internal static async Task CompleteAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = state.Subscription
            ? (object)new SubscriptionDeliveryCommand(id, new(state.Source, EventMessageSensitiveReplayProtocol.FreshGroup),
                state.Healthy.Get<ReceiveSubscriptionResult>().Deliveries.Single().Token, DeliveryAction.Ack)
            : new DeliveryCommand(id, state.Lane, state.Healthy.Get<ReceiveResult>().Deliveries.Single().Token, DeliveryAction.Ack);
        var kind = state.Subscription ? OperationKind.SubscriptionDelivery : OperationKind.Delivery;
        var receipt = EventMessageSensitiveReplayOperations.Apply(database, kind, request, state.Caller, id, token).Get<CommitReceipt>();
        var replay = EventMessageSensitiveReplayOperations.Apply(database, kind, request, state.Caller, id, token).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var emptyId = Guid.NewGuid();
        var empty = EventMessageSensitiveReplayOperations.Apply(database, state.ReceiveKind,
            EventMessageSensitiveReplayOperations.Receive(state, emptyId, true), state.Caller, emptyId, token);
        if (state.Subscription)
        { await Assert.That(empty.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty(); }
        else
        { await Assert.That(empty.Get<ReceiveResult>().Deliveries).IsEmpty(); }
    }
}
