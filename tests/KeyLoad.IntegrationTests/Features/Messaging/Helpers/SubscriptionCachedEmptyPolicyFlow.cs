using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionCachedEmptyPolicyFlow
{
    private const string HealthyEvent = "empty-policy-healthy";

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        SubscriptionCachedEmptyPolicyState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        { state = await StartAsync(fixture, sdk, mcp, seed, token); }, token);
        var original = state ?? throw new InvalidOperationException("The empty policy flow did not retain its original result.");
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => FirstColdAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterRf3Cold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => SecondColdAsync(sdk, mcp, original, token), token);
    }

    private static async Task<SubscriptionCachedEmptyPolicyState> StartAsync(ClusterFixture fixture,
        KeyLoadClient sdk, McpOfficialClient mcp, SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        var original = await SubscriptionCachedEmptyPublicFlow.StartAsync(sdk, mcp, seed, token);
        var group = original.Empty.Subscription;
        var denied = seed.Manager.Principal with
        {
            PolicyEpoch = checked(seed.Manager.Principal.PolicyEpoch + SubscriptionFilterRf3Protocol.Initial),
            Grants = [.. seed.Manager.Principal.Grants.Select(grant => grant.Resource == group.Source.Resource
                && grant.Database == group.Source.Partition.DatabaseId
                ? grant with { Capabilities = grant.Capabilities & ~Capability.SubscriptionsConsume } : grant)]
        };
        await MessagingRf3Identity.UpdateAsync(fixture, denied, token);
        await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, original.Empty,
            SubscriptionCachedEmptyPolicyAssertions.ScopeDenied, token);
        var blocked = SubscriptionPhasePublicAssertions.Receive(group);
        var revoked = await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, blocked,
            SubscriptionCachedEmptyPolicyAssertions.ScopeDenied, token);
        await Assert.That(await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, blocked,
            SubscriptionCachedEmptyPolicyAssertions.ScopeDenied, token)).IsEqualTo(revoked);
        await MessagingRf3Identity.UpdateAsync(fixture, seed.Manager.Principal with
        { PolicyEpoch = checked(denied.PolicyEpoch + SubscriptionFilterRf3Protocol.Initial) }, token);
        var emptyProblem = await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, original.Empty,
            SubscriptionCachedEmptyPolicyAssertions.EpochChanged, token);
        var deniedProblem = await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, blocked,
            SubscriptionCachedEmptyPolicyAssertions.EpochChanged, token);
        var state = new SubscriptionCachedEmptyPolicyState(original, blocked, revoked, emptyProblem, deniedProblem);
        await SubscriptionCachedEmptyPolicyAssertions.FencedAsync(sdk, mcp, state, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        return state;
    }

    private static async Task FirstColdAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPolicyState state, CancellationToken token)
    {
        await SubscriptionCachedEmptyPolicyAssertions.FencedAsync(sdk, mcp, state, token);
        var group = state.Original.Empty.Subscription;
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 4L, false, token);
        var request = SubscriptionPhasePublicAssertions.Receive(group);
        var claim = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, request, state.Original.Source, [5L], token);
        state.Claim = request;
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claim.Deliveries.Single().Token, DeliveryAction.Ack);
        state.Ack = ack;
        state.Acknowledged = await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 5L, token);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
        await SubscriptionCachedEmptyPolicyAssertions.FencedAsync(sdk, mcp, state, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 5L, false, token);
    }

    private static async Task SecondColdAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionCachedEmptyPolicyState state, CancellationToken token)
    {
        await SubscriptionCachedEmptyPolicyAssertions.FencedAsync(sdk, mcp, state, token);
        var group = state.Original.Empty.Subscription;
        var request = state.Claim ?? throw new InvalidOperationException("The repaired current-epoch claim is absent.");
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
        var ack = state.Ack ?? throw new InvalidOperationException("The repaired current-epoch ACK request is absent.");
        var receipt = state.Acknowledged ?? throw new InvalidOperationException("The original current-epoch ACK receipt is absent.");
        await SubscriptionFilterRf3Assertions.EqualAsync(
            await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, 5L, token), receipt);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(group.Source.Resource,
                [new(HealthyEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload)])]);
        var committed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publish, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, committed);
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(group.Source), token));
        await Assert.That(source.Events.Length).IsEqualTo(6);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Take(5).ToArray(), state.Original.Source.Events.ToArray());
        await Assert.That(source.Events.Last().Position).IsEqualTo(6L);
        await SubscriptionFilterRf3Assertions.EqualAsync(source.Events.Last().Data,
            new EventData(HealthyEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload));
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, source, 6L, token);
        await SubscriptionFilterRf3Assertions.HistoryAsync(sdk, mcp, group.Source, source, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(sdk, mcp, group, 1L, 6L, false, token);
        await Assert.That(await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, state.Original.Empty,
            SubscriptionCachedEmptyPolicyAssertions.EpochChanged, token)).IsEqualTo(state.EmptyPolicyProblem);
        await Assert.That(await SubscriptionCachedEmptyPolicyAssertions.RefuseAsync(sdk, mcp, state.Denied,
            SubscriptionCachedEmptyPolicyAssertions.EpochChanged, token)).IsEqualTo(state.DeniedPolicyProblem);
    }
}
