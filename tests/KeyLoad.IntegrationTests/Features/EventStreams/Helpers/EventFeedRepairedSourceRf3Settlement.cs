using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedRepairedSourceRf3Settlement
{
    internal static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> ReleaseAsync(
        TwoRf3MembershipWave wave, string credential,
        (EventFeedControlRequest Request, EventFeedControlResult Result) offered, CancellationToken token)
    {
        var acknowledge = offered.Request with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Acknowledge,
            ExpectedRevision = offered.Result.Revision,
            ExpectedCoverageGeneration = offered.Result.CoverageGeneration,
            Cursor = offered.Result.Cursor,
            OfferDigest = offered.Result.OfferDigest
        };
        await RequireRefusedAsync(wave, credential, acknowledge, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(offered.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offered.Request, token).ConfigureAwait(false));
        var release = acknowledge with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Release,
            OfferDigest = null
        };
        var result = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, release, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(release, result, offered.Result.Revision, released: true).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(result, await EventFeedSettlementRf3Flow.RoutesAsync(
            wave, credential, release, token).ConfigureAwait(false));
        return (release, result);
    }

    internal static async Task ReplayAsync(TwoRf3MembershipWave wave, string credential,
        (EventFeedControlRequest Request, EventFeedControlResult Result) offered,
        (EventFeedControlRequest Request, EventFeedControlResult Result) released, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(offered.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offered.Request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(released.Result, await EventFeedSettlementRf3Flow.RoutesAsync(
            wave, credential, released.Request, token).ConfigureAwait(false));
    }

    private static Task<EventFeedControlRequest> RequireRefusedAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var sdk = await callers.Sdk.AcknowledgeEventFeedAsync(request, token);
                await Assert.That(sdk.IsSuccess).IsFalse();
                await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
                    EventFeedSettlementProtocol.AcknowledgeTool, request, token), ErrorCode.TokenInvalidated, dispatched: true);
                var sql = await callers.Sdk.ExecuteSqlAsync(SqlRf3Protocol.Call(request.ControlPartition,
                    EventFeedSettlementProtocol.AcknowledgeTool, request), token);
                await Assert.That(sql.IsSuccess).IsFalse();
                await Assert.That(sql.Problem!.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
                return request;
            }, token);
}
