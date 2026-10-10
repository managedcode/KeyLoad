using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicReads
{
    internal static async Task RedactedAsync(RequestCqrsRf3Callers inspector, EventMessageSensitivePublicState state,
        bool dlq, CancellationToken token)
    {
        var payload = state.Header ? EventMessageSensitivePublicProtocol.Payload : EventMessageSensitivePublicProtocol.RedactedPayload;
        var headers = state.Header ? EventMessageSensitivePublicProtocol.RedactedHeaders : EventMessageSensitivePublicProtocol.Headers;
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            if (state.Subscription)
            {
                var request = new ReadEventSourceRequest(state.Source);
                var page = await QueueLifecyclePublicRoutes.CallAsync(inspector, route, state.Partition, McpCallerTools.EventsRead,
                    request, () => inspector.Sdk.ReadEventSourceAsync(request, token), token);
                var record = await Assert.That(page.Events).HasSingleItem();
                await Assert.That(record.Source).IsEqualTo(state.Source);
                await Assert.That(record.Data.EventId).IsEqualTo(EventMessageSensitivePublicProtocol.Message);
                await Assert.That(record.Data.PayloadJson).IsEqualTo(payload);
                await Assert.That(record.Data.HeadersJson).IsEqualTo(headers);
                await Assert.That(page.HasMore).IsFalse();
                await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(state.OriginalPage!.CutPosition);
                await Assert.That(NativeSerialization.Serialize(page.Head).SequenceEqual(NativeSerialization.Serialize(state.OriginalPage.Head))).IsTrue();
                var expected = state.OriginalPage.Events.Single() with
                { Data = state.OriginalPage.Events.Single().Data with { PayloadJson = payload, HeadersJson = headers } };
                await Assert.That(NativeSerialization.Serialize(record).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
            }
            else
            {
                var request = new InspectMessageRequest(state.Lane, EventMessageSensitivePublicProtocol.Message);
                var inspection = await QueueLifecyclePublicRoutes.CallAsync(inspector, route, state.Partition, McpCallerTools.MessagesInspect,
                    request, () => inspector.Sdk.InspectAsync(request, token), token);
                await Assert.That(inspection!.Metadata.Id).IsEqualTo(request.Id);
                await Assert.That(inspection.Metadata.State).IsEqualTo(dlq ? MessageState.DeadLettered : MessageState.Leased);
                await Assert.That(inspection.PayloadJson).IsEqualTo(payload);
                await Assert.That(inspection.HeadersJson).IsEqualTo(headers);
                if (!dlq)
                { await Assert.That(NativeSerialization.Serialize(inspection.Metadata).SequenceEqual(NativeSerialization.Serialize(state.OriginalInspection!.Metadata))).IsTrue(); }
                else
                {
                    var expected = state.OriginalInspection!.Metadata with
                    {
                        State = MessageState.DeadLettered,
                        StateVersion = state.OriginalInspection.Metadata.StateVersion + EventMessageSensitivePublicProtocol.VersionIncrement,
                        LeaseOwner = null,
                        LeaseUntil = null,
                        NotBefore = null,
                        SafeFailureCode = EventMessageSensitivePublicProtocol.AttemptsExhausted,
                        ParkedSequence = EventMessageSensitivePublicProtocol.InitialRevision
                    };
                    await Assert.That(NativeSerialization.Serialize(inspection.Metadata).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
                }
            }
        }
    }
}
