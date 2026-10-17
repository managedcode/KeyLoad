using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3Flow
{
    private const int OneDelivery = 1;
    private const int NoDeliveries = 0;
    private const string Invalid = "The original Tail frame does not contain a successful full result.";
    private const string Tool = "keyload_tail_event_feed";
    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest offered, EventFeedControlResult original, CancellationToken token)
    {
        var request = Request(offered, original);
        var policy = new CqrsStreamClientOptions { MaximumStreamBytes = new DatabaseLimits().MaxQueryReadBytes };
        await EventFeedTailRf3Cancellation.RunAsync(wave, credential, offered, original, token).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution(), Options.Create(policy));
        var progress = NoDeliveries;
        var completed = NoDeliveries;
        await foreach (var frame in sdk.TailEventFeedAsync(request, token).ConfigureAwait(false))
        {
            if (frame.Kind == CqrsStreamChunkKind.Progress)
            {
                ++progress;
                await ProgressAsync(frame, original);
            }
            else if (frame.Kind == CqrsStreamChunkKind.Completed)
            {
                ++completed;
                await CompletedAsync(frame, original);
            }
            else
            {
                await Assert.That(frame.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            }
        }
        await Assert.That(progress).IsEqualTo(OneDelivery);
        await Assert.That(completed).IsEqualTo(OneDelivery);
        await OfficialAsync(wave, credential, request with
        {
            CommandId = Guid.NewGuid()
        }, original, policy, token);
        await SqlRf3Protocol.EqualAsync(original, await EventFeedOfferRf3Flow.RoutesAsync(wave, credential, offered, token));
    }

    internal static EventFeedControlRequest Request(EventFeedControlRequest offered, EventFeedControlResult original)
        => offered with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Tail,
            ExpectedRevision = original.Revision,
            ExpectedCoverageGeneration = original.CoverageGeneration,
            Cursor = original.Cursor,
            OfferDigest = null,
            Limit = OneDelivery
        };

    private static async Task ProgressAsync(CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> frame,
        EventFeedControlResult original)
    {
        if (frame.ProgressResult is not { IsSuccess: true, Value: { } value })
        {
            throw new InvalidOperationException(Invalid);
        }
        await SqlRf3Protocol.EqualAsync(original, value.OriginalOfferResult);
    }

    private static async Task CompletedAsync(CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> frame,
        EventFeedControlResult original)
    {
        if (frame.Final is not { IsSuccess: true, Value: { } value })
        {
            throw new InvalidOperationException(Invalid);
        }
        await SqlRf3Protocol.EqualAsync(original, value);
    }

    private static async Task OfficialAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, EventFeedControlResult original, CqrsStreamClientOptions policy,
        CancellationToken token)
    {
        await using var caller = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, credential, token).ConfigureAwait(false);
        var progress = new EventFeedTailRf3Progress(policy.MaximumFrameBytes);
        var terminal = await caller.Client.CallToolAsync(McpCallerProtocol.GatewayInvoke,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpCallerProtocol.ToolId] = Tool,
                [McpCallerProtocol.Arguments] = McpOfficialClient.Arguments(request)
            }, progress: progress, cancellationToken: token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original,
            (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(terminal)).Value);
        await progress.RequireAsync(original);
    }
}
