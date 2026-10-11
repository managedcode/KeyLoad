using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedSettlementRf3Flow
{
    private const long RevisionStep = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest offered, EventFeedControlResult originalOffer, CancellationToken token)
    {
        var acknowledge = offered with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Acknowledge,
            ExpectedRevision = originalOffer.Revision,
            ExpectedCoverageGeneration = originalOffer.CoverageGeneration,
            Cursor = originalOffer.Cursor,
            OfferDigest = originalOffer.OfferDigest
        };
        var acknowledged = await RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(acknowledge, acknowledged,
            checked(originalOffer.Revision + RevisionStep), released: false).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(originalOffer, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offered, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(acknowledged, await RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false));
        var release = acknowledge with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Release,
            ExpectedRevision = acknowledged.Revision,
            Cursor = acknowledged.Cursor,
            OfferDigest = null
        };
        var released = await RoutesAsync(wave, credential, release, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(release, released, acknowledged.Revision, released: true).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(released, await RoutesAsync(wave, credential, release, token).ConfigureAwait(false));
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(acknowledged, await RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(released, await RoutesAsync(wave, credential, release, token).ConfigureAwait(false));
    }

    internal static Task<EventFeedControlResult> RoutesAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var acknowledgement = request.Action == EventFeedControlAction.Acknowledge;
                var tool = acknowledgement ? EventFeedSettlementProtocol.AcknowledgeTool : EventFeedSettlementProtocol.ReleaseTool;
                var sdk = acknowledgement ? await callers.Sdk.AcknowledgeEventFeedAsync(request, token)
                    : await callers.Sdk.ReleaseEventFeedAsync(request, token);
                var original = await McpCallerAssertions.SdkSuccessAsync(sdk);
                var official = (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(
                    await callers.Mcp.CallAsync(tool, request, token))).Value;
                await SqlRf3Protocol.EqualAsync(original, official);
                await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<EventFeedControlResult>(callers.Sdk,
                    SqlRf3Protocol.Call(request.ControlPartition, tool, request), token));
                return original;
            }, token);
}
