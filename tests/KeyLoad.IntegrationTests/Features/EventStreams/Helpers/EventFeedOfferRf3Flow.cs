using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedOfferRf3Flow
{
    private const int Limit = 1;

    internal static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> RunAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest opened,
        EventFeedControlResult original, PartitionRef local, PartitionRef remote, string first,
        string second, CancellationToken token)
    {
        var localPage = await SourceAsync(wave, credential, TwoRf3MembershipProtocol.Node1,
            new(local, opened.Scope.Resource, EventSourceKind.Topic), token).ConfigureAwait(false);
        var remotePage = await SourceAsync(wave, credential, TwoRf3MembershipProtocol.Node4,
            new(remote, opened.Scope.Resource, EventSourceKind.Topic), token).ConfigureAwait(false);
        await EventFeedOfferRf3Assertions.SourceAsync(localPage, first).ConfigureAwait(false);
        await EventFeedOfferRf3Assertions.SourceAsync(remotePage, second).ConfigureAwait(false);
        var request = opened with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Offer,
            ExpectedRevision = original.Revision,
            ExpectedCoverageGeneration = original.CoverageGeneration,
            Cursor = original.Cursor,
            OfferDigest = null,
            Limit = Limit
        };
        var offered = await RoutesAsync(wave, credential, request, token).ConfigureAwait(false);
        await EventFeedOfferRf3Assertions.OfferAsync(request, offered, [localPage, remotePage]).ConfigureAwait(false);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                await EventFeedOfferRf3Assertions.DeniedAsync(callers, request with { CommandId = Guid.NewGuid() },
                    ErrorCode.Conflict, token).ConfigureAwait(false);
                await EventFeedOfferRf3Assertions.DeniedAsync(callers, request with { Limit = checked(Limit + Limit) },
                    ErrorCode.Conflict, token).ConfigureAwait(false);
                return request;
            }, token).ConfigureAwait(false);
        return (request, offered);
    }

    internal static Task<EventFeedControlResult> RoutesAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OfferEventFeedAsync(request, token));
                var official = (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(
                    await callers.Mcp.CallAsync(EventFeedOfferProtocol.Tool, request, token))).Value;
                await SqlRf3Protocol.EqualAsync(original, official);
                await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<EventFeedControlResult>(callers.Sdk,
                    SqlRf3Protocol.Call(request.ControlPartition, EventFeedOfferProtocol.Tool, request), token));
                return original;
            }, token);

    private static async Task<EventSourcePage> SourceAsync(TwoRf3MembershipWave wave, string credential,
        string node, EventSourceRef source, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(
            new(source, Limit: Limit), token)).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(source, actual.Source);
        return actual;
    }
}
