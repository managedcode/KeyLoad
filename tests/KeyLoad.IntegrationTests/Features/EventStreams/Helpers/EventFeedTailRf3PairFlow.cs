using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3PairFlow
{
    private const int TwoDeliveries = 2;
    private const string Tool = "keyload_tail_event_feed";
    internal static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> RunAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest offered,
        EventFeedControlResult original, PartitionRef remote, CommitReceipt lateReceipt, string secondEvent, string thirdEvent,
        CancellationToken token)
    {
        var sdkPair = await SdkAsync(wave, credential, offered, original, remote, lateReceipt, secondEvent, token).ConfigureAwait(false);
        return await OfficialAsync(wave, credential, sdkPair.Request, sdkPair.Result,
            remote, thirdEvent, token).ConfigureAwait(false);
    }

    private static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> SdkAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest offered,
        EventFeedControlResult original, PartitionRef remote, CommitReceipt? originalSourceReceipt, string nextEvent, CancellationToken token)
    {
        var policy = new CqrsStreamClientOptions { MaximumStreamBytes = new DatabaseLimits().MaxQueryReadBytes };
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution(), Options.Create(policy));
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new EventFeedTailRf3Pair(policy.MaximumFrameBytes);
        var request = EventFeedTailRf3Flow.Request(offered, original) with { Limit = TwoDeliveries };
        var resumeConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualOperation = EventFeedTailRf3PairOperations.SdkAsync(sdk, request, progress, resumeConsumer.Task, owned.Token);
        return await EventFeedTailRf3PairJoin.RunAsync(actualOperation, static value => ValueTask.FromResult(value), progress, original,
            NextWhilePausedAsync, owned, token).ConfigureAwait(false);

        async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> NextWhilePausedAsync()
        {
            var next = await EventFeedTailRf3NextOffer.RunAsync(wave, credential, offered, original, remote,
                nextEvent, originalSourceReceipt, publish: false, token).ConfigureAwait(false);
            await progress.RequirePausedAsync(original).ConfigureAwait(false);
            resumeConsumer.SetResult();
            return next;
        }
    }

    private static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> OfficialAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest offered,
        EventFeedControlResult original, PartitionRef remote, string nextEvent, CancellationToken token)
    {
        var policy = new CqrsStreamClientOptions { MaximumStreamBytes = new DatabaseLimits().MaxQueryReadBytes };
        await using var caller = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, credential, token).ConfigureAwait(false);
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new EventFeedTailRf3Pair(policy.MaximumFrameBytes);
        var request = EventFeedTailRf3Flow.Request(offered, original) with { Limit = TwoDeliveries };
        var actualCall = caller.Client.CallToolAsync(McpCallerProtocol.GatewayInvoke,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            { [McpCallerProtocol.ToolId] = Tool, [McpCallerProtocol.Arguments] = McpOfficialClient.Arguments(request) },
            progress: progress, cancellationToken: owned.Token).AsTask();
        try
        {
            return await EventFeedTailRf3PairJoin.RunAsync(actualCall,
                async value => (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(value)).Value, progress, original,
                () => EventFeedTailRf3NextOffer.RunAsync(wave, credential, offered, original, remote,
                    nextEvent, originalSourceReceipt: null, publish: true, token), owned, token).ConfigureAwait(false);
        }
        finally
        {
            // The owner above retains every original native fault; visibly join that SAME SDK call before disposing its client.
            await ((Task)actualCall).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
