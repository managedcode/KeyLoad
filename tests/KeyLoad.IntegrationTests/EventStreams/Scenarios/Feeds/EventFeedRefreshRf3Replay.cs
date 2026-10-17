using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class EventFeedRefreshRf3Replay(
    (EventFeedControlRequest Request, EventFeedControlResult Result)[] originals)
{
    internal async Task RequireAsync(TwoRf3MembershipWave wave, string credential, CancellationToken token)
    {
        foreach (var original in originals)
        {
            var actual = original.Request.Action switch
            {
                EventFeedControlAction.Open => await EventFeedFirstOpenRf3Flow.RoutesAsync(
                    wave, credential, original.Request, token).ConfigureAwait(false),
                EventFeedControlAction.Refresh => await EventFeedRefreshRf3Operations.RoutesAsync(
                    wave, credential, original.Request, token).ConfigureAwait(false),
                EventFeedControlAction.Offer => await EventFeedOfferRf3Flow.RoutesAsync(
                    wave, credential, original.Request, token).ConfigureAwait(false),
                _ => await EventFeedSettlementRf3Flow.RoutesAsync(
                    wave, credential, original.Request, token).ConfigureAwait(false)
            };
            await SqlRf3Protocol.EqualAsync(original.Result, actual).ConfigureAwait(false);
            if (original.Request.Action == EventFeedControlAction.Offer)
            {
                await EventFeedTailRf3Refusal.RunAsync(wave, credential, original.Request, original.Result,
                    ErrorCode.RecoveryRequired, token).ConfigureAwait(false);
            }
        }
    }
}
