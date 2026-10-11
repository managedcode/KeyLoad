using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3Cancellation
{
    private const int OneDelivery = 1;
    private const int NoDeliveries = 0;
    private const int TwoDeliveries = 2;
    private const string Tool = "keyload_tail_event_feed";
    private const string Invalid = "The original cancellable Tail has no complete first delivery.";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest offered, EventFeedControlResult original, CancellationToken token)
    {
        var request = EventFeedTailRf3Flow.Request(offered, original) with { Limit = TwoDeliveries };
        var policy = new CqrsStreamClientOptions { MaximumStreamBytes = new DatabaseLimits().MaxQueryReadBytes };
        await SdkAsync(wave, credential, request, original, policy, token).ConfigureAwait(false);
        await OfficialAsync(wave, credential, request with { CommandId = Guid.NewGuid() }, original, policy, token)
            .ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await EventFeedOfferRf3Flow.RoutesAsync(wave, credential, offered, token));
    }

    private static async Task SdkAsync(TwoRf3MembershipWave wave, string credential, EventFeedControlRequest request,
        EventFeedControlResult original, CqrsStreamClientOptions policy, CancellationToken token)
    {
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution(), Options.Create(policy));
        var deliveries = NoDeliveries;
        var cancelled = false;
        try
        {
            await foreach (var frame in sdk.TailEventFeedAsync(request, owned.Token).ConfigureAwait(false))
            {
                await Assert.That(frame.Kind).IsEqualTo(CqrsStreamChunkKind.Progress);
                if (frame.ProgressResult is not { IsSuccess: true, Value: { } value })
                { throw new InvalidOperationException(Invalid); }
                await SqlRf3Protocol.EqualAsync(original, value.OriginalOfferResult);
                ++deliveries;
                await owned.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (owned.IsCancellationRequested && !token.IsCancellationRequested)
        { cancelled = true; }
        await Assert.That(deliveries).IsEqualTo(OneDelivery);
        await Assert.That(cancelled).IsTrue();
    }

    private static async Task OfficialAsync(TwoRf3MembershipWave wave, string credential, EventFeedControlRequest request,
        EventFeedControlResult original, CqrsStreamClientOptions policy, CancellationToken token)
    {
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var caller = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, credential, token).ConfigureAwait(false);
        var progress = new EventFeedTailRf3Progress(policy.MaximumFrameBytes);
        var operation = caller.Client.CallToolAsync(McpCallerProtocol.GatewayInvoke,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpCallerProtocol.ToolId] = Tool,
                [McpCallerProtocol.Arguments] = McpOfficialClient.Arguments(request)
            }, progress: progress, cancellationToken: owned.Token).AsTask();
        await EventFeedTailRf3CancellationJoin.JoinAsync(operation, progress, original, owned, request.Limit, token).ConfigureAwait(false);
    }
}
