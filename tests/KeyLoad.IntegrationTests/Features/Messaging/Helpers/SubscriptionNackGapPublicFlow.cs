using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionNackGapPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        SubscriptionNackGapPublicState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        { state = await StartAsync(sdk, mcp, seed, token); }, token);
        var original = state ?? throw new InvalidOperationException("The original Nack gap flow is absent.");
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => NackAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => ReclaimAsync(sdk, mcp, original, token), token);
    }

    private static async Task<SubscriptionNackGapPublicState> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        var request = SubscriptionPhasePublicAssertions.Receive(seed.Independent);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(seed.Independent.Source), token));
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
            await SubscriptionFilterRf3Assertions.EqualAsync(source.Events[index].Source, seed.Independent.Source);
        }
        var claimed = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, request, source, [1L, 2L, 3L], token);
        var state = new SubscriptionNackGapPublicState(request, claimed, source);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), seed.Independent, claimed.Deliveries[2].Token, DeliveryAction.Ack);
        state.History.Add((ack, await SubscriptionNackGapPublicAssertions.CompleteAsync(sdk, mcp, ack, 3L, 1L, token)));
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, seed.Independent, 1L, 0L, false, token);
        await SubscriptionNackGapPublicAssertions.OriginalAsync(sdk, mcp, state, token);
        return state;
    }

    private static async Task NackAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionNackGapPublicState state, CancellationToken token)
    {
        var group = state.Request.Subscription;
        await SubscriptionNackGapPublicAssertions.OriginalAsync(sdk, mcp, state, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.Claimed.Deliveries[1].Token, DeliveryAction.Ack);
        state.History.Add((ack, await SubscriptionNackGapPublicAssertions.CompleteAsync(sdk, mcp, ack, 2L, 1L, token)));
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        var nack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.Claimed.Deliveries[0].Token, DeliveryAction.Nack);
        state.History.Add((nack, await SubscriptionNackGapPublicAssertions.CompleteAsync(sdk, mcp, nack, 1L, 1L, token)));
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        await SubscriptionNackGapPublicAssertions.StaleAsync(sdk, mcp, state, token);
    }

    private static async Task ReclaimAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionNackGapPublicState state, CancellationToken token)
    {
        var group = state.Request.Subscription;
        await SubscriptionNackGapPublicAssertions.StaleAsync(sdk, mcp, state, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        await Task.Delay(new SubscriptionPolicy().RetryBaseMilliseconds, token);
        var request = SubscriptionPhasePublicAssertions.Receive(group);
        var claimed = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value, claimed);
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await SubscriptionFilterRf3Assertions.EqualAsync(delivery.Event, state.Source.Events.Single(e => e.Position == 1L));
        await Assert.That(delivery.LeaseVersion).IsEqualTo(2L);
        await Assert.That(delivery.Attempt).IsEqualTo(2);
        await Assert.That(delivery.Token).IsNotEqualTo(state.Claimed.Deliveries[0].Token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        await SubscriptionNackGapPublicAssertions.StaleAsync(sdk, mcp, state, token);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        state.History.Add((ack, await SubscriptionNackGapPublicAssertions.CompleteAsync(sdk, mcp, ack, 1L, 2L, token)));
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 3L, false, token);
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, state.Source, 4L, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        await SubscriptionNackGapPublicAssertions.StaleAsync(sdk, mcp, state, token);
    }
}
