using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionFilterClusterDelivery
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterClusterOriginal original,
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
        await McpCallerAssertions.SdkSuccessAsync(await manager.SetSubscriptionPausedAsync(new(Guid.NewGuid(), seed.Group, SubscriptionFilterClusterProtocol.Updated, false), token));
        var claimed = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group, SubscriptionFilterClusterProtocol.Window), token));
        await Assert.That(claimed.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(new long[] { 2, 4 }, CollectionOrdering.Matching);
        await McpCallerAssertions.SdkSuccessAsync(await worker.CompleteSubscriptionAsync(new(Guid.NewGuid(), seed.Group, claimed.Deliveries.Last().Token, DeliveryAction.Ack), token));
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Updated, SubscriptionFilterClusterProtocol.Gap, false, token);
        await McpCallerAssertions.SdkSuccessAsync(await worker.CompleteSubscriptionAsync(new(Guid.NewGuid(), seed.Group, claimed.Deliveries.First().Token, DeliveryAction.Ack), token));
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Updated, SubscriptionFilterClusterProtocol.Completed, false, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Independent, SubscriptionFilterClusterProtocol.Initial, 0, false, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
    }
}
