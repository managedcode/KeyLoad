using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionFilterClusterHealthy
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterClusterUpdated updated, CancellationToken token)
    {
        var seed = updated.Original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var manager = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.ConfigureSubscriptionAsync(updated.Request, token)), updated.Result);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, updated.Request, token))).Value, updated.Result);
        await SubscriptionFilterClusterSqlReplay.RunAsync(manager, mcp, updated, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Updated, SubscriptionFilterClusterProtocol.Completed, false, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(manager, mcp, seed.Group.Source, updated.Original.History, token);
        var publish = new CommandRequest(Guid.NewGuid(), seed.Group.Source.Partition,
            [new PublishTopic(SubscriptionFilterClusterProtocol.Topic,
                [new(SubscriptionFilterClusterProtocol.Healthy, SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Payload)])]);
        await McpCallerAssertions.SdkSuccessAsync(await manager.CommitAsync(publish, token));
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), seed.Group);
        var delivery = (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, receive, token))).Value.Deliveries.Single();
        await Assert.That(delivery.Event.Data.EventId).IsEqualTo(SubscriptionFilterClusterProtocol.Healthy);
        var processing = new SubscriptionProcessingRequest(Guid.NewGuid(), seed.Group, delivery.Token, SubscriptionFilterClusterProtocol.Handler,
            SubscriptionFilterClusterProtocol.Initial, [new PutDocument(SubscriptionFilterClusterProtocol.Collection, SubscriptionFilterClusterProtocol.Healthy, SubscriptionFilterClusterProtocol.Payload)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await manager.CommitSubscriptionProcessingAsync(processing, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, processing, token))).Value, original);
        var document = await PublicReadAssertions.DocumentAsync(await manager.GetAsync(new(seed.Group.Source.Partition, SubscriptionFilterClusterProtocol.Collection, SubscriptionFilterClusterProtocol.Healthy), token));
        await Assert.That(document!.Json).IsEqualTo(SubscriptionFilterClusterProtocol.Payload);
        await Assert.That(document.Revision).IsEqualTo(SubscriptionFilterClusterProtocol.Initial);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Updated, SubscriptionFilterClusterProtocol.HealthyCheckpoint, false, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await manager.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group), token));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
