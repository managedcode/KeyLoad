using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveReplayAssertions
{
    internal static async Task DeniedAsync(OperationResult result, ErrorCode code)
    {
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.SafeDetail).DoesNotContain("SENSITIVE_BODY_NATIVE_CANARY");
        await Assert.That(result.SafeDetail).DoesNotContain("SENSITIVE_HEADER_NATIVE_CANARY");
    }

    internal static async Task RefusalAsync(DatabaseEngine database, ZoneTreeStore store,
        EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var originalState = State(database, state);
        await DeniedAsync(EventMessageSensitiveReplayOperations.Apply(database, state.ReceiveKind,
            state.OriginalRequest, state.Caller, state.OriginalId, token), ErrorCode.PermissionDenied);
        await Assert.That(EventMessageSensitiveReplayOperations.Outcome(store, state).SequenceEqual(state.OriginalOutcome)).IsTrue();
        await Assert.That(State(database, state).SequenceEqual(originalState)).IsTrue();
    }

    internal static byte[] State(DatabaseEngine database, EventMessageSensitiveReplayState state)
        => state.Subscription ? NativeSerialization.Serialize(database.GetSubscription(EventMessageSensitiveReplayProtocol.Root, state.OriginalGroup))
            : NativeSerialization.Serialize(database.InspectMessage(EventMessageSensitiveReplayProtocol.Root, state.Lane, EventMessageSensitiveReplayProtocol.First)!);

    internal static async Task FullAsync(OperationResult result, EventMessageSensitiveReplayState state)
    {
        string payload;
        string headers;
        if (state.Subscription)
        {
            var delivery = await Assert.That(result.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
            await Assert.That(delivery.Event.Data.EventId).IsEqualTo(EventMessageSensitiveReplayProtocol.First);
            await Assert.That(delivery.Event.Source).IsEqualTo(state.Source);
            await Assert.That(delivery.Event.Data.EventType).IsEqualTo(EventMessageSensitiveReplayProtocol.EventType);
            payload = delivery.Event.Data.PayloadJson;
            headers = delivery.Event.Data.HeadersJson;
        }
        else
        {
            var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
            payload = delivery.PayloadJson;
            headers = delivery.HeadersJson;
        }
        await Assert.That(payload).IsEqualTo(EventMessageSensitiveReplayProtocol.Payload);
        await Assert.That(headers).IsEqualTo(EventMessageSensitiveReplayProtocol.Headers);
    }

    internal static async Task RedactedAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var expectedPayload = state.Header ? EventMessageSensitiveReplayProtocol.Payload : EventMessageSensitiveReplayProtocol.RedactedPayload;
        var expectedHeaders = state.Header ? EventMessageSensitiveReplayProtocol.RedactedHeaders : EventMessageSensitiveReplayProtocol.Headers;
        if (state.Subscription)
        {
            var record = await Assert.That(database.ReadEventSource(state.Worker.Id, new(state.Source), token).Events).HasSingleItem();
            await Assert.That(record.Data.PayloadJson).IsEqualTo(expectedPayload);
            await Assert.That(record.Data.HeadersJson).IsEqualTo(expectedHeaders);
        }
        else
        {
            var inspection = database.InspectMessage(state.Worker.Id, state.Lane, EventMessageSensitiveReplayProtocol.First)!;
            await Assert.That(inspection.PayloadJson).IsEqualTo(expectedPayload);
            await Assert.That(inspection.HeadersJson).IsEqualTo(expectedHeaders);
            await Assert.That(inspection.Metadata.State).IsEqualTo(MessageState.Leased);
        }
    }

    internal static async Task HealthyAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var replay = EventMessageSensitiveReplayOperations.Apply(database, state.ReceiveKind,
            state.HealthyRequest, state.Caller, state.HealthyId, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(state.Healthy))).IsTrue();
        var producerReplay = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Batch,
            state.Producer, EventMessageSensitiveReplayProtocol.Root, state.Producer.CommandId, token);
        await Assert.That(NativeSerialization.Serialize(producerReplay).SequenceEqual(NativeSerialization.Serialize(state.Produced))).IsTrue();
        await FullAsync(replay, state);
    }
}
