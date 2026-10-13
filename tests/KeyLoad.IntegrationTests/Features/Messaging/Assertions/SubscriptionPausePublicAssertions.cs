using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionPausePublicAssertions
{
    internal static async Task OriginalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionPausePublicState state, CancellationToken token)
    {
        var group = state.Claim.Subscription;
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SetSubscriptionPausedAsync(state.Pause, token)), state.Paused);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, state.Pause, token))).Value, state.Paused);
        foreach (var entry in state.History)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CompleteSubscriptionAsync(entry.Request, token)), entry.Receipt);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, entry.Request, token))).Value, entry.Receipt);
        }
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Claim, ErrorCode.StaleLease, token);
        await Assert.That(await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp,
            state.Denied, ErrorCode.DispatchPaused, token)).IsEqualTo(state.DeniedProblem);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, group.Source, state.Source, token);
    }
}
