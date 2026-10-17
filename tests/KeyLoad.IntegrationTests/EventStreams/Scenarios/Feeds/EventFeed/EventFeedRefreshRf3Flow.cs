using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedRefreshRf3Flow
{
    private const string Topic = "feed-refresh-topic";
    private const string First = "refresh-first";
    private const string Second = "refresh-second";
    private const string Late = "refresh-late";
    private const long Step = 1;

    internal static async Task<EventFeedRefreshRf3Replay> RunAsync(TwoRf3MembershipWave wave,
        string credential, EventFeedControlRequest source, PartitionRef remote, CancellationToken token)
    {
        var (opened, empty) = await OpenEmptyThenPublishAsync(wave, credential, source, remote, token).ConfigureAwait(false);
        var refresh = opened with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Refresh,
            ExpectedRevision = empty.Revision,
            ExpectedCoverageGeneration = empty.CoverageGeneration,
            Cursor = empty.Cursor
        };
        var refreshed = await EventFeedRefreshRf3Operations.RoutesAsync(wave, credential, refresh, token).ConfigureAwait(false);
        await RequireRefreshedAsync(refresh, refreshed, empty).ConfigureAwait(false);
        var offered = await EventFeedOfferRf3Flow.RunAsync(wave, credential, opened, refreshed,
            opened.ControlPartition, remote, First, Second, token).ConfigureAwait(false);
        await EventFeedRefreshRf3Operations.RefusedAsync(wave, credential, refresh with
        {
            CommandId = Guid.NewGuid(),
            ExpectedRevision = refreshed.Revision,
            ExpectedCoverageGeneration = refreshed.CoverageGeneration,
            Cursor = refreshed.Cursor
        }, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(offered.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offered.Request, token).ConfigureAwait(false));
        var acknowledge = offered.Request with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Acknowledge
        };
        var acknowledged = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(acknowledge, acknowledged,
            checked(refreshed.Revision + Step), released: false).ConfigureAwait(false);
        var next = refresh with
        {
            CommandId = Guid.NewGuid(),
            ExpectedRevision = acknowledged.Revision,
            ExpectedCoverageGeneration = acknowledged.CoverageGeneration,
            Cursor = acknowledged.Cursor
        };
        var current = await EventFeedRefreshRf3Operations.RoutesAsync(wave, credential, next, token).ConfigureAwait(false);
        await RequireRefreshedAsync(next, current, acknowledged).ConfigureAwait(false);
        await EventFeedTailRf3Refusal.RunAsync(wave, credential, offered.Request, offered.Result,
            ErrorCode.TokenInvalidated, token).ConfigureAwait(false);
        var finalAcknowledged = await EventFeedRefreshRf3Operations.RequireAcknowledgedAsync(wave, credential, opened, current,
            remote, token).ConfigureAwait(false);
        var release = next with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Release,
            ExpectedRevision = finalAcknowledged.Revision,
            ExpectedCoverageGeneration = finalAcknowledged.CoverageGeneration,
            Cursor = finalAcknowledged.Cursor
        };
        var released = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, release, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(release, released, finalAcknowledged.Revision, released: true).ConfigureAwait(false);
        await EventFeedRefreshRf3Operations.PublishAsync(wave, credential, TwoRf3MembershipProtocol.Node4,
            remote, Topic, Late, token).ConfigureAwait(false);
        var replay = new EventFeedRefreshRf3Replay([(opened, empty), (refresh, refreshed),
            (offered.Request, offered.Result), (acknowledge, acknowledged), (next, current), (release, released)]);
        await replay.RequireAsync(wave, credential, token).ConfigureAwait(false);
        return replay;
    }

    private static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> OpenEmptyThenPublishAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest source,
        PartitionRef remote, CancellationToken token)
    {
        var opened = source with
        {
            CommandId = Guid.NewGuid(),
            MapId = Guid.NewGuid(),
            Scope = source.Scope with
            {
                Resource = Topic
            }
        };
        await EventFeedRefreshRf3Operations.ConfigureAsync(wave, credential, opened.ControlPartition,
            remote, Topic, token).ConfigureAwait(false);
        var empty = await EventFeedFirstOpenRf3Flow.RoutesAsync(wave, credential, opened, token).ConfigureAwait(false);
        await EventFeedFirstOpenRf3Assertions.OpenAsync(opened, empty).ConfigureAwait(false);
        await EventFeedRefreshRf3Operations.PublishAsync(wave, credential, TwoRf3MembershipProtocol.Node1,
            opened.ControlPartition, Topic, First, token).ConfigureAwait(false);
        await EventFeedRefreshRf3Operations.PublishAsync(wave, credential, TwoRf3MembershipProtocol.Node4,
            remote, Topic, Second, token).ConfigureAwait(false);
        return (opened, empty);
    }

    private static Task RequireRefreshedAsync(EventFeedControlRequest request,
        EventFeedControlResult actual, EventFeedControlResult previous)
        => EventFeedSettlementRf3Assertions.ResultAsync(request with
        {
            ExpectedCoverageGeneration = checked(previous.CoverageGeneration + Step)
        }, actual, checked(previous.Revision + Step), released: false);
}
