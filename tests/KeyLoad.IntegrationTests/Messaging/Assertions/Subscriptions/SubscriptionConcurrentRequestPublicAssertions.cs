using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionConcurrentRequestPublicAssertions
{
    internal static async Task<ReceiveSubscriptionResult> ConcurrentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReceiveSubscriptionRequest request, CancellationToken token)
    {
        var sdkFailures = new List<Exception>();
        var mcpFailures = new List<Exception>();
        ReceiveSubscriptionResult? first = null;
        ReceiveSubscriptionResult? second = null;
        var sdkCall = global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            first = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request, token));
        }, sdkFailures);
        var mcpCall = global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            second = (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value;
        }, mcpFailures);
        await Task.WhenAll(sdkCall, mcpCall);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny([.. sdkFailures, .. mcpFailures]);
        var original = first ?? throw new InvalidOperationException("The original concurrent SDK result is absent.");
        var replay = second ?? throw new InvalidOperationException("The original concurrent MCP result is absent.");
        await SubscriptionFilterRf3Assertions.EqualAsync(original, replay);
        return original;
    }

    internal static async Task OriginalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionConcurrentRequestPublicState state, bool firstCurrent, EventSourcePage source, CancellationToken token)
    {
        foreach (var saved in state.History)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CompleteSubscriptionAsync(saved.Request, token)), saved.Receipt);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, saved.Request, token))).Value, saved.Receipt);
        }
        if (firstCurrent)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ReceiveSubscriptionAsync(state.First, token)), state.FirstResult);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.First, token))).Value, state.FirstResult);
        }
        else
        {
            await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.First, ErrorCode.StaleLease, token);
        }
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Remaining, ErrorCode.StaleLease, token);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveSubscriptionAsync(state.Empty, token)), state.EmptyResult);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.Empty, token))).Value, state.EmptyResult);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, state.First.Subscription.Source, source, token);
    }

    internal static async Task LiteralSourceAsync(EventSourcePage source)
    {
        await Assert.That(source.Events.Length).IsEqualTo(4);
        await Assert.That(source.Head.TailPosition).IsEqualTo(4L);
        var expected = new EventData[] {
            new(SubscriptionFilterRf3Protocol.FirstEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload),
            new(SubscriptionFilterRf3Protocol.SecondEvent, SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Payload),
            new(SubscriptionFilterRf3Protocol.ThirdEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload),
            new(SubscriptionFilterRf3Protocol.FourthEvent, SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Payload) };
        for (var index = 0; index < expected.Length; index++)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(source.Events[index].Data, expected[index]);
            await Assert.That(source.Events[index].Position).IsEqualTo(index + 1L);
        }
    }
}
