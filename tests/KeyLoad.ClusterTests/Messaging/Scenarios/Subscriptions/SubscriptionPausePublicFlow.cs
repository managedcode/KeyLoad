using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionPausePublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        SubscriptionPausePublicState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        {
            state = await StartAsync(sdk, mcp, seed, token);
        }, token);
        var original = state ?? throw new InvalidOperationException("The paused original flow did not finish.");
        await SubscriptionFilterClusterCold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => DrainAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterClusterCold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => FinishAsync(sdk, mcp, original, token), token);
    }

    private static async Task<SubscriptionPausePublicState> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        var group = seed.Independent;
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        var request = SubscriptionPhasePublicAssertions.Receive(group);
        var result = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, request, source, [1L, 2L, 3L], token);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1L, true);
        var paused = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(pause, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, pause, token))).Value, paused);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 0L, true, token);
        var denied = SubscriptionPhasePublicAssertions.Receive(group);
        var problem = await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, denied, ErrorCode.DispatchPaused, token);
        var state = new SubscriptionPausePublicState(request, result, pause, paused, denied, problem, source);
        var renew = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, result.Deliveries[0].Token,
            DeliveryAction.Renew, new SubscriptionPolicy().MaxLeaseSeconds);
        var receipt = await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, renew, 1L, token);
        state.History.Add((renew, receipt));
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveSubscriptionAsync(request, token)), result);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, result.Deliveries[2].Token, DeliveryAction.Ack);
        state.History.Add((ack, await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 3L, token)));
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 0L, true, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, group.Source, source, token);
        return state;
    }

    private static async Task DrainAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionPausePublicState state, CancellationToken token)
    {
        var group = state.Claim.Subscription;
        await SubscriptionPausePublicAssertions.OriginalAsync(sdk, mcp, state, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 0L, true, token);
        foreach (var index in new[] { 0, 1 })
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.Claimed.Deliveries[index].Token, DeliveryAction.Ack);
            state.History.Add((ack, await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, index + 1L, token)));
            await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, index == 0 ? 1L : 3L, true, token);
        }
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1L, false);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(resume, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, resume, token))).Value, result);
        state.Resume = resume;
        state.Resumed = result;
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 3L, false, token);
        await Assert.That(await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp,
            state.Denied, ErrorCode.DispatchPaused, token)).IsEqualTo(state.DeniedProblem);
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, state.Source, 4L, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
    }

    private static async Task FinishAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionPausePublicState state, CancellationToken token)
    {
        var group = state.Claim.Subscription;
        await SubscriptionPausePublicAssertions.OriginalAsync(sdk, mcp, state, token);
        var resume = state.Resume ?? throw new InvalidOperationException("The same-generation resume is absent.");
        var resumed = state.Resumed ?? throw new InvalidOperationException("The original same-generation resume result is absent.");
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SetSubscriptionPausedAsync(resume, token)), resumed);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, resume, token))).Value, state.Resumed);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource, [new(SubscriptionFilterClusterProtocol.Healthy,
                SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, receipt);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(source.Events.Last().Position).IsEqualTo(5L);
        await SubscriptionFilterClusterAssertions.EqualAsync(source.Events.Last().Data,
            new EventData(SubscriptionFilterClusterProtocol.Healthy, SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload));
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, source, 5L, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 1L, 5L, false, token);
        await Assert.That(await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp,
            state.Denied, ErrorCode.DispatchPaused, token)).IsEqualTo(state.DeniedProblem);
    }
}
