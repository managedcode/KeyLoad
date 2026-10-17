using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Delivery
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Original original,
        KeyLoadClient manager, McpOfficialClient mcp, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var worker = new KeyLoadClient(http, seed.Second.Secret, IntegrationClientOptions.Execution());
        var stale = new SubscriptionDeliveryCommand(Guid.NewGuid(), seed.Group, original.Second.Deliveries.First().Token, DeliveryAction.Ack);
        var denied = await worker.CompleteSubscriptionAsync(stale, token);
        await Assert.That(denied.Problem!.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
        var oldReceive = await worker.ReceiveSubscriptionAsync(original.SecondRequest, token);
        await Assert.That(oldReceive.Problem!.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
        var paused = await worker.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group), token);
        await Assert.That(paused.Problem!.ErrorCode).IsEqualTo(ErrorCode.DispatchPaused.ToString());
        await McpCallerAssertions.SdkSuccessAsync(await manager.SetSubscriptionPausedAsync(new(Guid.NewGuid(), seed.Group, SubscriptionFilterRf3Protocol.Updated, false), token));
        var claimed = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group, SubscriptionFilterRf3Protocol.Window), token));
        await Assert.That(claimed.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(new long[] { 2, 4 }, CollectionOrdering.Matching);
        await McpCallerAssertions.SdkSuccessAsync(await worker.CompleteSubscriptionAsync(new(Guid.NewGuid(), seed.Group, claimed.Deliveries.Last().Token, DeliveryAction.Ack), token));
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Updated, SubscriptionFilterRf3Protocol.Gap, false, token);
        await McpCallerAssertions.SdkSuccessAsync(await worker.CompleteSubscriptionAsync(new(Guid.NewGuid(), seed.Group, claimed.Deliveries.First().Token, DeliveryAction.Ack), token));
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Updated, SubscriptionFilterRf3Protocol.Completed, false, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Independent, SubscriptionFilterRf3Protocol.Initial, 0, false, token);
        await SubscriptionFilterRf3Assertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
    }
}
