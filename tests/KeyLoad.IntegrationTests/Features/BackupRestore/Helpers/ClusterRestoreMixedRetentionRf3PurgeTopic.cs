using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3PurgeTopic
{
    internal static EventData[] Events() =>
        [new("purge-first", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload),
         new("purge-second", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload),
         new("purge-third", ClusterRestoreMixedRetentionRf3Protocol.AcceptedType, ClusterRestoreMixedRetentionRf3Protocol.EventPayload)];

    internal static async Task SeedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        state.PurgedPublish = new(Guid.NewGuid(), state.Partition,
            [new PublishTopic(ClusterRestoreMixedRetentionRf3Protocol.Purged, [.. Events()])]);
        state.PurgedPublishReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.DocumentsCommit, state.PurgedPublish, () => callers.Sdk.CommitAsync(state.PurgedPublish, token), token);
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.PurgeGroup, new(state.Principal.Id));
        _ = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsConfigure, configure, () => callers.Sdk.ConfigureSubscriptionAsync(configure, token), token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(state.PurgedSource), token));
        await ClusterRestoreMixedRetentionRf3Literal.PageAsync(before, state.PurgedSource, Events(),
            ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration);
        var refused = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PurgeTopic(ClusterRestoreMixedRetentionRf3Protocol.Purged, ClusterRestoreMixedRetentionRf3Protocol.PurgePosition)]);
        await QueueLifecyclePublicRoutes.RefusedAsync(callers, state.OriginalQueue, refused, ErrorCode.ResourceExhausted, token);
        var unchanged = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(state.PurgedSource), token));
        await SqlRf3Protocol.EqualAsync(before.Events, unchanged.Events);
        await SqlRf3Protocol.EqualAsync(before.Head, unchanged.Head);
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.PurgeGroup,
            ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration, SubscriptionStart.FromNow);
        state.PurgeInfo = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.SubscriptionsSeek, seek, () => callers.Sdk.SeekSubscriptionAsync(seek, token), token);
        await SqlRf3Protocol.EqualAsync(new SubscriptionInfo(state.PurgeGroup, new(state.Principal.Id),
            QueueLifecyclePublicProtocol.Two, QueueLifecyclePublicProtocol.Two,
            ClusterRestoreMixedRetentionRf3Protocol.PurgedTail, ClusterRestoreMixedRetentionRf3Protocol.PurgedTail,
            ClusterRestoreMixedRetentionRf3Protocol.PurgedTail, true, null), state.PurgeInfo);
        state.Purge = refused with { CommandId = Guid.NewGuid() };
        state.PurgeReceipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.OriginalQueue.Route, state.Partition,
            McpCallerTools.DocumentsCommit, state.Purge, () => callers.Sdk.CommitAsync(state.Purge, token), token);
        state.PurgedPage = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(
            new(state.PurgedSource, AfterPosition: ClusterRestoreMixedRetentionRf3Protocol.PurgePosition), token));
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(ClusterRestoreMixedRetentionRf3Protocol.PurgedTail,
            ClusterRestoreMixedRetentionRf3Protocol.PurgedTail, ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration), state.PurgedPage.Head);
        await SqlRf3Protocol.EqualAsync(before.Events.Skip((int)ClusterRestoreMixedRetentionRf3Protocol.PurgePosition).ToArray(), state.PurgedPage.Events.ToArray());
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, McpCallerTools.DocumentsCommit,
            state.PurgedPublish, state.PurgedPublishReceipt, () => callers.Sdk.CommitAsync(state.PurgedPublish, token), token);
        await ClusterRestoreMixedRetentionRf3Literal.FourAsync(callers, state.Partition, McpCallerTools.DocumentsCommit,
            state.Purge, state.PurgeReceipt, () => callers.Sdk.CommitAsync(state.Purge, token), token);
        await ClusterRestoreMixedRetentionRf3Restore.RefusedAsync(callers, state, McpCallerTools.EventsRead,
            new ReadEventSourceRequest(state.PurgedSource), () => callers.Sdk.ReadEventSourceAsync(new(state.PurgedSource), token),
            ErrorCode.HistoryUnavailable, token);
    }
}
