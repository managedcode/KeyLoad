using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionCachedEmptyPublicFlow
{
    private const string Group = "cached-empty";
    private const string Fifth = "cached-empty-five";
    private const string Sixth = "cached-empty-six";

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        SubscriptionCachedEmptyPublicState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        { state = await StartAsync(sdk, mcp, seed, token); }, token);
        var original = state ?? throw new InvalidOperationException("The original empty subscription flow is absent.");
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => FirstColdAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => SecondColdAsync(sdk, mcp, original, token), token);
    }

    internal static async Task<SubscriptionCachedEmptyPublicState> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        var group = new SubscriptionRef(seed.Group.Source, Group);
        var definition = new SubscriptionDefinition(seed.Manager.Principal.Id);
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), group, definition, SubscriptionStart.FromNow);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureSubscriptionAsync(configure, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, configure, token))).Value, configured);
        await SubscriptionFilterRf3Assertions.EqualAsync(configured, new SubscriptionInfo(group, definition,
            1L, 1L, 4L, 4L, 4L, false, null));
        var empty = SubscriptionPhasePublicAssertions.Receive(group);
        var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(empty, token));
        await Assert.That(original.RequestId).IsEqualTo(empty.RequestId);
        await Assert.That(original.Deliveries).IsEmpty();
        await SubscriptionFilterRf3Assertions.EqualAsync(original.Status, configured);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, empty, token))).Value, original);
        var retained = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(retained.Events.Length).IsEqualTo(4);
        var publish = Publish(group, Fifth);
        var published = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, published);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(source.Events.Length).IsEqualTo(5);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Take(4).ToArray(), retained.Events.ToArray());
        await Assert.That(source.Events.Last().Position).IsEqualTo(5L);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Last().Data,
            new EventData(Fifth, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload));
        var state = new SubscriptionCachedEmptyPublicState(configure, configured, empty, original, publish, published, source);
        await OriginalAsync(sdk, mcp, state, token);
        return state;
    }

    private static async Task FirstColdAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPublicState state, CancellationToken token)
    {
        await OriginalAsync(sdk, mcp, state, token);
        var group = state.Empty.Subscription;
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        var request = SubscriptionPhasePublicAssertions.Receive(group);
        var claimed = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, request, state.Source, [5L], token);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claimed.Deliveries.Single().Token, DeliveryAction.Ack);
        state.Ack = ack;
        state.Acknowledged = await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 5L, token);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
        await OriginalAsync(sdk, mcp, state, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 5L, false, token);
    }

    private static async Task SecondColdAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPublicState state, CancellationToken token)
    {
        await OriginalAsync(sdk, mcp, state, token);
        var group = state.Empty.Subscription;
        var ack = state.Ack ?? throw new InvalidOperationException("The original ACK request is absent.");
        var acknowledged = state.Acknowledged ?? throw new InvalidOperationException("The original ACK receipt is absent.");
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 5L, token), acknowledged);
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), group, 1L, SubscriptionStart.FromNow);
        var sought = await McpCallerAssertions.SdkSuccessAsync(await sdk.SeekSubscriptionAsync(seek, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsSeek, seek, token))).Value, sought);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Empty, ErrorCode.TokenInvalidated, token);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 2L, false);
        var resumed = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(resume, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, resume, token))).Value, resumed);
        var publish = Publish(group, Sixth);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, receipt);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(source.Events.Length).IsEqualTo(6);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Take(5).ToArray(), state.Source.Events.ToArray());
        await Assert.That(source.Events.Last().Position).IsEqualTo(6L);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Last().Data,
            new EventData(Sixth, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload));
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, source, 6L, token);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, group.Source, source, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 2L, 6L, false, token);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Empty, ErrorCode.TokenInvalidated, token);
    }

    private static async Task OriginalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPublicState state, CancellationToken token)
    {
        var group = state.Empty.Subscription;
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(group, token));
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(state.Empty, token)), state.Original);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.Empty, token))).Value, state.Original);
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(group, token)), before);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ConfigureSubscriptionAsync(state.Configure, token)), state.Configured);
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(state.Publish, token)), state.Published);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Publish, token))).Value, state.Published);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, group.Source, state.Source, token);
    }

    private static CommandRequest Publish(SubscriptionRef group, string id)
        => new(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource,
                [new(id, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload)])]);
}
