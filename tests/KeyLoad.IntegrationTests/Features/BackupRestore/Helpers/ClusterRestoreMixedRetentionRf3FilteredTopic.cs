using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3FilteredTopic
{
    internal static EventData[] Events() =>
        [new("filtered-first", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload),
         new("filtered-skipped", ClusterRestoreMixedRetentionRf3Protocol.SkippedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload),
         new("filtered-third", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload),
         new("filtered-fourth", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload)];

    internal static SubscriptionDefinition Definition(ClusterRestoreMixedRetentionRf3State state)
        => new(state.Principal.Id) { EventTypes = [ClusterRestoreMixedRetentionRf3Protocol.AcceptedType] };

    internal static async Task SeedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        state.FilteredPublish = new(Guid.NewGuid(), state.Partition,
            [new PublishTopic(ClusterRestoreMixedRetentionRf3Protocol.Filtered, [.. Events()])]);
        state.FilteredPublishReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.DocumentsCommit, state.FilteredPublish, () => callers.Sdk.CommitAsync(state.FilteredPublish, token), token);
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.FilteredGroup, Definition(state));
        _ = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsConfigure, configure, () => callers.Sdk.ConfigureSubscriptionAsync(configure, token), token);
        state.FilteredPage = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(state.FilteredSource), token));
        await ClusterRestoreMixedRetentionRf3Literal.PageAsync(state.FilteredPage, state.FilteredSource, Events(),
            ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration);
        state.FilteredRequest = new(Guid.NewGuid(), state.FilteredGroup, MaxEvents: ClusterRestoreMixedRetentionRf3Protocol.MatchingEvents);
        state.FilteredReceived = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsReceive, state.FilteredRequest, () => callers.Sdk.ReceiveSubscriptionAsync(state.FilteredRequest, token), token);
        await SqlRf3Protocol.EqualAsync(state.FilteredPage.Events.Where(record => record.Data.EventType
            == ClusterRestoreMixedRetentionRf3Protocol.AcceptedType).ToArray(), state.FilteredReceived.Deliveries.Select(delivery => delivery.Event).ToArray());
        state.FilteredAck = new(Guid.NewGuid(), state.FilteredGroup,
            state.FilteredReceived.Deliveries.Single(delivery => delivery.Event.Data.EventId == "filtered-third").Token, DeliveryAction.Ack);
        state.FilteredAckReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsComplete, state.FilteredAck, () => callers.Sdk.CompleteSubscriptionAsync(state.FilteredAck, token), token);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.FilteredGroup, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration, true);
        state.FilteredInfo = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsPause, pause, () => callers.Sdk.SetSubscriptionPausedAsync(pause, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, McpCallerTools.DocumentsCommit,
            state.FilteredPublish, state.FilteredPublishReceipt, () => callers.Sdk.CommitAsync(state.FilteredPublish, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, McpCallerTools.SubscriptionsComplete,
            state.FilteredAck, state.FilteredAckReceipt, () => callers.Sdk.CompleteSubscriptionAsync(state.FilteredAck, token), token);
        await SqlRf3Protocol.EqualAsync(new SubscriptionInfo(state.FilteredGroup, Definition(state),
            ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration,
            ClusterRestoreMixedRetentionRf3Protocol.NoRevision, ClusterRestoreMixedRetentionRf3Protocol.FilteredTail,
            ClusterRestoreMixedRetentionRf3Protocol.FilteredTail, true, null), state.FilteredInfo);
    }

    internal static async Task ContinueAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.FilteredGroup, state.FilteredInfo.Generation, SubscriptionStart.FromBeginning);
        var expected = state.FilteredInfo with
        {
            Generation = state.FilteredInfo.Generation + ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration,
            OwnershipEpoch = state.FilteredInfo.OwnershipEpoch + ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration,
            IssuedPosition = ClusterRestoreMixedRetentionRf3Protocol.NoRevision
        };
        await SqlRf3Protocol.EqualAsync(expected, await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsSeek, seek, () => callers.Sdk.SeekSubscriptionAsync(seek, token), token));
        var unpause = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.FilteredGroup, expected.Generation, false);
        await SqlRf3Protocol.EqualAsync(expected with { Paused = false }, await QueueLifecyclePublicRoutes.CallAsync(callers,
            state.CurrentQueue.Route, state.Partition, McpCallerTools.SubscriptionsPause, unpause,
            () => callers.Sdk.SetSubscriptionPausedAsync(unpause, token), token));
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.FilteredGroup, MaxEvents: ClusterRestoreMixedRetentionRf3Protocol.MatchingEvents);
        var received = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsReceive, request, () => callers.Sdk.ReceiveSubscriptionAsync(request, token), token);
        await SqlRf3Protocol.EqualAsync(state.FilteredPage.Events.Where(record => record.Data.EventType
            == ClusterRestoreMixedRetentionRf3Protocol.AcceptedType).ToArray(), received.Deliveries.Select(delivery => delivery.Event).ToArray());
        foreach (var delivery in received.Deliveries)
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.FilteredGroup, delivery.Token, DeliveryAction.Ack);
            var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.CurrentQueue.Route, state.Partition,
                McpCallerTools.SubscriptionsComplete, ack, () => callers.Sdk.CompleteSubscriptionAsync(ack, token), token);
            state.CurrentSubscriptionAcks.Add((ack, receipt));
        }
        state.CurrentFilteredInfo = expected with
        {
            Paused = false,
            Checkpoint = ClusterRestoreMixedRetentionRf3Protocol.FilteredTail,
            IssuedPosition = ClusterRestoreMixedRetentionRf3Protocol.FilteredTail
        };
        await SqlRf3Protocol.EqualAsync(state.CurrentFilteredInfo,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SubscriptionStatusAsync(state.FilteredGroup, token)));
    }
}
