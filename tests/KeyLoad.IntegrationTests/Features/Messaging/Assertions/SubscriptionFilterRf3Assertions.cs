using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Assertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task StatusAsync(KeyLoadClient manager, McpOfficialClient mcp, SubscriptionRef group,
        long generation, long checkpoint, bool paused, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await manager.SubscriptionStatusAsync(group, token));
        var official = (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(
            McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(group), token))).Value;
        await EqualAsync(sdk, official);
        await Assert.That(sdk.Generation).IsEqualTo(generation);
        await Assert.That(sdk.OwnershipEpoch).IsEqualTo(generation);
        await Assert.That(sdk.Checkpoint).IsEqualTo(checkpoint);
        await Assert.That(sdk.Paused).IsEqualTo(paused);
    }

    internal static async Task HistoryAsync(KeyLoadClient manager, McpOfficialClient mcp,
        EventSourceRef source, EventSourcePage expected, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await manager.ReadEventSourceAsync(new(source), token));
        var official = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await mcp.CallAsync(
            McpCallerTools.EventsRead, new ReadEventSourceRequest(source), token))).Value;
        await Assert.That(sdk.Events).IsEquivalentTo(expected.Events, CollectionOrdering.Matching);
        await Assert.That(official.Events).IsEquivalentTo(expected.Events, CollectionOrdering.Matching);
        await Assert.That(sdk.Head).IsEqualTo(expected.Head);
        await Assert.That(official.Head).IsEqualTo(expected.Head);
    }

    internal static async Task RefusedAsync(KeyLoadClient manager, McpOfficialClient mcp,
        ConfigureSubscriptionRequest request, ErrorCode error, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await manager.SubscriptionStatusAsync(request.Subscription, token));
        var failed = await manager.ConfigureSubscriptionAsync(request, token);
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Problem!.ErrorCode).IsEqualTo(error.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, request, token), error, true);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.SubscriptionStatusAsync(request.Subscription, token)), before);
    }
}
