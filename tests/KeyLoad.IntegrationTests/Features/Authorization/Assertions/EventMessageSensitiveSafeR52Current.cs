using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitiveSafeR52Current
{
    internal static async Task CaptureAsync(RequestCqrsRf3Callers root, EventMessageSensitiveSafeR52State state,
        CancellationToken token)
    {
        if (state.Model.Subscription)
        {
            state.Page = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadEventSourceAsync(new(state.Model.Source), token));
            await LiteralPageAsync(state.Page, state);
        }
        else
        {
            foreach (var id in state.Published)
            {
                var reply = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(new(state.Model.Lane, id), token));
                if (state.Acknowledged.Contains(id) && state.Messages.TryGetValue(id, out var prior) && prior is not null)
                {
                    var expected = prior with
                    {
                        Metadata = prior.Metadata with
                        {
                            State = MessageState.Acked,
                            ActiveOrderSequence = EventMessageSensitiveSafeR52Protocol.EmptyCount,
                            StateVersion = EventMessageSensitiveSafeR52Protocol.AckedVersion,
                            LeaseOwner = null,
                            LeaseUntil = null
                        },
                        PayloadJson = null,
                        HeadersJson = null
                    };
                    await SqlRf3Protocol.EqualAsync(expected, reply.Message);
                }
                state.Messages[id] = reply.Message;
                await LiteralMessageAsync(reply.Message, id, state);
            }
        }
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Model.Partition, token));
        await RequireAsync(root, state, token);
    }

    internal static async Task RequireAsync(RequestCqrsRf3Callers root, EventMessageSensitiveSafeR52State state,
        CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            if (state.Model.Subscription)
            {
                var request = new ReadEventSourceRequest(state.Model.Source);
                var actual = await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Model.Partition,
                    McpCallerTools.EventsRead, request, () => root.Sdk.ReadEventSourceAsync(request, token), token);
                await LiteralPageAsync(actual, state);
                await Assert.That(actual.CutPosition).IsGreaterThan((long)EventMessageSensitiveSafeR52Protocol.EmptyCount);
                await SqlRf3Protocol.EqualAsync(state.Page! with { CutPosition = actual.CutPosition }, actual);
            }
            else
            {
                foreach (var id in state.Published)
                {
                    var request = new InspectMessageRequest(state.Model.Lane, id);
                    var actual = await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Model.Partition,
                        McpCallerTools.MessagesInspect, request, () => root.Sdk.InspectAsync(request, token), token);
                    await LiteralMessageAsync(actual.Message, id, state);
                    await SqlRf3Protocol.EqualAsync(state.Messages[id], actual.Message);
                }
            }
        }
        var placement = new AtomicPartitionPlacementReadRequest(EventMessageSensitiveSafeR52Protocol.PlacementSchema, state.Model.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await root.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Model.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Model.Partition), token))).Value);
    }

    private static async Task LiteralPageAsync(EventSourcePage page, EventMessageSensitiveSafeR52State state)
    {
        await Assert.That(page.HasMore).IsFalse();
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(state.Published.Count,
            EventMessageSensitiveSafeR52Protocol.FirstPosition, state.Model.Source.Generation), page.Head);
        await Assert.That(page.Events.Length).IsEqualTo(state.Published.Count);
        for (var index = 0; index < state.Published.Count; index++)
        {
            var item = page.Events[index];
            await Assert.That(item.Source).IsEqualTo(state.Model.Source);
            await Assert.That(item.Data.EventId).IsEqualTo(state.Published[index]);
            await Assert.That(item.Data.EventType).IsEqualTo(EventMessageSensitiveSafeR52Protocol.EventType);
            await Assert.That(item.Data.PayloadJson).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Payload);
            await Assert.That(item.Data.HeadersJson).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Headers);
        }
    }

    private static async Task LiteralMessageAsync(MessageInspection? inspection, string id, EventMessageSensitiveSafeR52State state)
    {
        await Assert.That(inspection).IsNotNull();
        await Assert.That(inspection!.Metadata.Id).IsEqualTo(id);
        var expectedState = state.Acknowledged.Contains(id) ? MessageState.Acked
            : state.Leased.Contains(id) ? MessageState.Leased : MessageState.Ready;
        await Assert.That(inspection.Metadata.State).IsEqualTo(expectedState);
        await Assert.That(inspection.Metadata.StateVersion).IsEqualTo(expectedState == MessageState.Ready
            ? EventMessageSensitiveSafeR52Protocol.ReadyVersion : expectedState == MessageState.Leased
                ? EventMessageSensitiveSafeR52Protocol.LeasedVersion : EventMessageSensitiveSafeR52Protocol.AckedVersion);
        if (state.Acknowledged.Contains(id))
        {
            await Assert.That(inspection.PayloadJson).IsNull();
            await Assert.That(inspection.HeadersJson).IsNull();
        }
        else
        {
            await Assert.That(inspection.PayloadJson).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Payload);
            await Assert.That(inspection.HeadersJson).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Headers);
        }
    }
}
