using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionConcurrentRequestPublicFlow
{
    private const string HealthyEvent = "concurrent-request-healthy";

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        SubscriptionConcurrentRequestPublicState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        {
            state = await StartAsync(sdk, mcp, seed, token);
        }, token);
        var original = state ?? throw new InvalidOperationException("The concurrent original request flow is absent.");
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => DrainAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => FinishAsync(sdk, mcp, original, token), token);
    }

    private static async Task<SubscriptionConcurrentRequestPublicState> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        var group = seed.Independent;
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await SubscriptionConcurrentRequestPublicAssertions.LiteralSourceAsync(source);
        var first = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1,
            LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds);
        var claimed = await SubscriptionConcurrentRequestPublicAssertions.ConcurrentAsync(sdk, mcp, first, token);
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await SubscriptionFilterRf3Assertions.EqualAsync(delivery.Event, source.Events.Single(e => e.Position == 1L));
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        var remaining = SubscriptionPhasePublicAssertions.Receive(group);
        var rest = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, remaining, source, [2L, 3L], token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(group, token));
        var empty = SubscriptionPhasePublicAssertions.Receive(group);
        var emptyResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(empty, token));
        await Assert.That(emptyResult.Deliveries).IsEmpty();
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, empty, token))).Value, emptyResult);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(group, token)), before);
        var state = new SubscriptionConcurrentRequestPublicState(first, claimed, remaining, rest, empty, emptyResult, source);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, rest.Deliveries.Last().Token, DeliveryAction.Ack);
        state.History.Add((ack, await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 3L, token)));
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        await SubscriptionConcurrentRequestPublicAssertions.OriginalAsync(sdk, mcp, state, true, source, token);
        return state;
    }

    private static async Task DrainAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionConcurrentRequestPublicState state, CancellationToken token)
    {
        var group = state.First.Subscription;
        await SubscriptionConcurrentRequestPublicAssertions.OriginalAsync(sdk, mcp, state, true, state.Source, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 0L, false, token);
        var requests = new[] {
            new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.FirstResult.Deliveries.Single().Token, DeliveryAction.Ack),
            new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.RemainingResult.Deliveries.First().Token, DeliveryAction.Ack) };
        for (var index = 0; index < requests.Length; index++)
        {
            state.History.Add((requests[index], await SubscriptionPhasePublicAssertions.CompleteAsync(
                sdk, mcp, requests[index], index + 1L, token)));
            await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, index == 0 ? 1L : 3L, false, token);
        }
        var fourthRequest = SubscriptionPhasePublicAssertions.Receive(group);
        var fourth = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, fourthRequest, state.Source, [4L], token);
        var fourthAck = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, fourth.Deliveries.Single().Token, DeliveryAction.Ack);
        state.History.Add((fourthAck, await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, fourthAck, 4L, token)));
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, fourthRequest, ErrorCode.StaleLease, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        await SubscriptionConcurrentRequestPublicAssertions.OriginalAsync(sdk, mcp, state, false, state.Source, token);
    }

    private static async Task FinishAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionConcurrentRequestPublicState state, CancellationToken token)
    {
        var group = state.First.Subscription;
        await SubscriptionConcurrentRequestPublicAssertions.OriginalAsync(sdk, mcp, state, false, state.Source, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource, [new(HealthyEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, receipt);
        await Assert.That(receipt.CommandId).IsEqualTo(publish.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("publishTopic", group.Source.Resource, HealthyEvent, 5L) }, CollectionOrdering.Matching);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(source.Events.Length).IsEqualTo(5);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Take(4).ToArray(), state.Source.Events.ToArray());
        await Assert.That(source.Events.Last().Position).IsEqualTo(5L);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Last().Data,
            new EventData(HealthyEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload));
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, source, 5L, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 5L, false, token);
        await SubscriptionConcurrentRequestPublicAssertions.OriginalAsync(sdk, mcp, state, false, source, token);
    }
}
