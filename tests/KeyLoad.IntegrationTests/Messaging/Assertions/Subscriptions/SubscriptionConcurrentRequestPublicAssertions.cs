using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

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
        await SubscriptionFilterClusterAssertions.EqualAsync(original, replay);
        return original;
    }

    internal static async Task OriginalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionConcurrentRequestPublicState state, bool firstCurrent, EventSourcePage source, CancellationToken token)
    {
        foreach (var saved in state.History)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CompleteSubscriptionAsync(saved.Request, token)), saved.Receipt);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, saved.Request, token))).Value, saved.Receipt);
        }
        if (firstCurrent)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ReceiveSubscriptionAsync(state.First, token)), state.FirstResult);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.First, token))).Value, state.FirstResult);
        }
        else
        {
            await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.First, ErrorCode.StaleLease, token);
        }
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Remaining, ErrorCode.StaleLease, token);
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveSubscriptionAsync(state.Empty, token)), state.EmptyResult);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.Empty, token))).Value, state.EmptyResult);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, state.First.Subscription.Source, source, token);
    }

    internal static async Task LiteralSourceAsync(EventSourcePage source)
    {
        await Assert.That(source.Events.Length).IsEqualTo(4);
        await Assert.That(source.Head.TailPosition).IsEqualTo(4L);
        var expected = new EventData[] {
            new(SubscriptionFilterClusterProtocol.FirstEvent, SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload),
            new(SubscriptionFilterClusterProtocol.SecondEvent, SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Payload),
            new(SubscriptionFilterClusterProtocol.ThirdEvent, SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload),
            new(SubscriptionFilterClusterProtocol.FourthEvent, SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Payload) };
        for (var index = 0; index < expected.Length; index++)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(source.Events[index].Data, expected[index]);
            await Assert.That(source.Events[index].Position).IsEqualTo(index + 1L);
        }
    }
}
