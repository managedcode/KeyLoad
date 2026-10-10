using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Literal
{
    internal static async Task FourAsync<T>(RequestCqrsRf3Callers callers, PartitionRef partition, string tool,
        object request, T expected, Func<Task<Result<T>>> sdk, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            await SqlRf3Protocol.EqualAsync(expected, await QueueLifecyclePublicRoutes.CallAsync(callers, route,
                partition, tool, request, sdk, token));
        }
    }

    internal static Task AbsentDocumentAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state,
        string collection, string id, CancellationToken token)
    {
        var reference = new EntityRef(state.Partition, collection, id);
        return FourAsync<DocumentResult?>(callers, state.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), null, () => callers.Sdk.GetAsync(reference, token), token);
    }

    internal static async Task PageAsync(EventSourcePage actual, EventSourceRef source, EventData[] expected,
        long first, long generation)
    {
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(expected.Length, first, generation), actual.Head);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNotEmpty();
        await Assert.That(actual.CutPosition).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
        await Assert.That(actual.Events.Length).IsEqualTo(expected.Length);
        for (var index = ClusterRestoreMixedRetentionRf3Protocol.FirstIndex; index < expected.Length; index++)
        {
            var row = actual.Events[index];
            await Assert.That(row.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            await Assert.That(row.EventSequence).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
            await SqlRf3Protocol.EqualAsync(new SourceEventRecord(source, index + first, row.EventSequence, expected[index], row.RecordedAt), row);
        }
    }

    internal static async Task OriginalAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.InitialAsync(callers, state.OriginalQueue, token);
        await TopicsAsync(callers, state, token);
        await InboxAsync(callers, state, token);
        var delivery = state.InputReceived.Deliveries.Single();
        await SqlRf3Protocol.EqualAsync(new MessageInspection(new(ClusterRestoreMixedRetentionRf3Protocol.OriginalInput,
            MessageState.Leased, QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Two, QueueLifecyclePublicProtocol.One,
            null, null, state.Principal.Id, delivery.LeaseVersion, delivery.LeaseUntil),
            ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders), state.InputImage);
    }

    internal static async Task TopicsAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        await PageRoutesAsync(callers, state, new ReadEventSourceRequest(state.FilteredSource), state.FilteredPage, token);
        await PageRoutesAsync(callers, state,
            new ReadEventSourceRequest(state.PurgedSource, AfterPosition: ClusterRestoreMixedRetentionRf3Protocol.PurgePosition),
            state.PurgedPage, token);
        await FourAsync(callers, state.Partition, McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.FilteredGroup),
            state.Continued ? state.CurrentFilteredInfo : state.FilteredInfo,
            () => callers.Sdk.SubscriptionStatusAsync(state.FilteredGroup, token), token);
        await FourAsync(callers, state.Partition, McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.PurgeGroup),
            state.PurgeInfo, () => callers.Sdk.SubscriptionStatusAsync(state.PurgeGroup, token), token);
    }

    private static async Task PageRoutesAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state,
        ReadEventSourceRequest request, EventSourcePage expected, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.EventsRead,
                request, () => callers.Sdk.ReadEventSourceAsync(request, token), token);
            await Assert.That(actual.Cursor).IsNotEmpty();
            await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
            await SqlRf3Protocol.EqualAsync(expected with { Cursor = actual.Cursor, CutPosition = actual.CutPosition }, actual);
        }
    }

    internal static async Task InboxAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var input = new InspectMessageRequest(state.Source, ClusterRestoreMixedRetentionRf3Protocol.OriginalInput);
        await FourAsync<MessageInspection?>(callers, state.Source.Partition, McpCallerTools.MessagesInspect, input,
            state.Continued ? state.FinalOriginalInput : state.InputImage, () => callers.Sdk.InspectAsync(input, token), token);
        await EffectAsync(callers, state, ClusterRestoreMixedRetentionRf3Protocol.OriginalEffect,
            state.OutputImage, state.DocumentImage, state.InboxEvents, token);
        if (state.Continued)
        {
            var current = new InspectMessageRequest(state.Source, ClusterRestoreMixedRetentionRf3Protocol.FreshInput);
            await FourAsync<MessageInspection?>(callers, state.Source.Partition, McpCallerTools.MessagesInspect, current,
                state.FreshInputImage, () => callers.Sdk.InspectAsync(current, token), token);
            await EffectAsync(callers, state, ClusterRestoreMixedRetentionRf3Protocol.FreshEffect,
                new(new(ClusterRestoreMixedRetentionRf3Protocol.FreshEffect, MessageState.Ready, ClusterRestoreMixedRetentionRf3Protocol.NoAttempts,
                    QueueLifecyclePublicProtocol.One, QueueLifecyclePublicProtocol.Two, null, null),
                    ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders),
                new(new(state.Partition, ClusterRestoreMixedRetentionRf3Protocol.Documents, ClusterRestoreMixedRetentionRf3Protocol.FreshEffect),
                    ClusterRestoreMixedRetentionRf3Protocol.FirstRevision, ClusterRestoreMixedRetentionRf3Protocol.EventPayload, false, []),
                state.FreshInboxEvents, token);
        }
    }

    private static async Task EffectAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, string effect,
        MessageInspection output, DocumentResult document, System.Collections.Immutable.ImmutableArray<EventRecord> events, CancellationToken token)
    {
        var inspect = new InspectMessageRequest(new(state.Partition, ClusterRestoreMixedRetentionRf3Protocol.Output), effect);
        await FourAsync<MessageInspection?>(callers, state.Partition, McpCallerTools.MessagesInspect, inspect, output,
            () => callers.Sdk.InspectAsync(inspect, token), token);
        await FourAsync<DocumentResult?>(callers, state.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(document.Reference),
            document, () => callers.Sdk.GetAsync(document.Reference, token), token);
        var stream = ClusterRestoreMixedRetentionRf3Inbox.Stream(state, effect);
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(new(stream), token));
        await SqlRf3Protocol.EqualAsync(new StreamHead(ClusterRestoreMixedRetentionRf3Protocol.FirstRevision,
            ClusterRestoreMixedRetentionRf3Protocol.FirstRevision, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration), page.Head);
        var row = await Assert.That(events).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(new EventRecord(stream, ClusterRestoreMixedRetentionRf3Protocol.FirstRevision,
            row.EventSequence, new(effect, ClusterRestoreMixedRetentionRf3Protocol.AcceptedType,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload, ClusterRestoreMixedRetentionRf3Protocol.EventHeaders), row.RecordedAt), row);
        await Assert.That(row.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        await Assert.That(row.EventSequence).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
        await SqlRf3Protocol.EqualAsync(events, page.Events);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.StreamsRead,
                new ReadStreamRequest(stream), () => callers.Sdk.ReadStreamAsync(new(stream), token), token);
            await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(page.CutPosition);
            await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
            await Assert.That(actual.Cursor).IsNull();
            await SqlRf3Protocol.EqualAsync(page with
            {
                Cursor = actual.Cursor,
                CutPosition = actual.CutPosition,
                SnapshotCutPosition = actual.SnapshotCutPosition
            }, actual);
        }
    }
}
