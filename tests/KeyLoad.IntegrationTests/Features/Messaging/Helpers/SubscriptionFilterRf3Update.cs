using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Update
{
    internal static async Task<SubscriptionFilterRf3Updated> RunAsync(ClusterFixture fixture,
        SubscriptionFilterRf3Original original, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var manager = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        var first = new KeyLoadClient(http, seed.First.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
        await using var second = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, seed.Second.Secret, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Initial, SubscriptionFilterRf3Protocol.Gap, true, token);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.CommitAsync(seed.Publish, token)), seed.PublishReceipt);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await first.CompleteSubscriptionAsync(original.AckFirst, token)), original.FirstReceipt);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await second.CallAsync(McpCallerTools.SubscriptionsComplete, original.AckThird, token))).Value, original.ThirdReceipt);
        await SubscriptionFilterRf3Assertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
        await SubscriptionFilterRf3Refusals.RunAsync(manager, mcp, seed, token);
        await SubscriptionFilterRf3Policy.RunAsync(fixture, original, manager, mcp, token);
        var request = seed.Replacement with { CommandId = Guid.NewGuid() };
        var updated = (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, request, token))).Value;
        await Assert.That(updated.IssuedPosition).IsEqualTo(SubscriptionFilterRf3Protocol.Gap);
        await SubscriptionFilterRf3Assertions.EqualAsync(updated.Definition, request.Definition);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Updated, SubscriptionFilterRf3Protocol.Gap, true, token);
        await SubscriptionFilterRf3Delivery.RunAsync(fixture, original, manager, mcp, token);
        return new(original, request, updated);
    }
}
