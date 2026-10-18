using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterClusterUpdate
{
    internal static async Task<SubscriptionFilterClusterUpdated> RunAsync(ClusterFixture fixture,
        SubscriptionFilterClusterOriginal original, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var manager = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        var first = new KeyLoadClient(http, seed.First.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
        await using var second = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, seed.Second.Secret, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Initial, SubscriptionFilterClusterProtocol.Gap, true, token);
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.CommitAsync(seed.Publish, token)), seed.PublishReceipt);
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await first.CompleteSubscriptionAsync(original.AckFirst, token)), original.FirstReceipt);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await second.CallAsync(McpCallerTools.SubscriptionsComplete, original.AckThird, token))).Value, original.ThirdReceipt);
        await SubscriptionFilterClusterAssertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
        await SubscriptionFilterClusterRefusals.RunAsync(manager, mcp, seed, token);
        await SubscriptionFilterClusterPolicy.RunAsync(fixture, original, manager, mcp, token);
        var request = seed.Replacement with
        {
            CommandId = Guid.NewGuid()
        };
        var updated = (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, request, token))).Value;
        await Assert.That(updated.IssuedPosition).IsEqualTo(SubscriptionFilterClusterProtocol.Gap);
        await SubscriptionFilterClusterAssertions.EqualAsync(updated.Definition, request.Definition);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Updated, SubscriptionFilterClusterProtocol.Gap, true, token);
        await SubscriptionFilterClusterDelivery.RunAsync(fixture, original, manager, mcp, token);
        return new(original, request, updated);
    }
}
