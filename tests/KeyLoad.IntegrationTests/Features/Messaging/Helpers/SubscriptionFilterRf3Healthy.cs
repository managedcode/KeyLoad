using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Healthy
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Updated updated, CancellationToken token)
    {
        var seed = updated.Original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var manager = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.ConfigureSubscriptionAsync(updated.Request, token)), updated.Result);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, updated.Request, token))).Value, updated.Result);
        await SubscriptionFilterRf3SqlReplay.RunAsync(manager, mcp, updated, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Updated, SubscriptionFilterRf3Protocol.Completed, false, token);
        await SubscriptionFilterRf3Assertions.HistoryAsync(manager, mcp, seed.Group.Source, updated.Original.History, token);
        var publish = new CommandRequest(Guid.NewGuid(), seed.Group.Source.Partition,
            [new PublishTopic(SubscriptionFilterRf3Protocol.Topic,
                [new(SubscriptionFilterRf3Protocol.Healthy, SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Payload)])]);
        await McpCallerAssertions.SdkSuccessAsync(await manager.CommitAsync(publish, token));
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), seed.Group);
        var delivery = (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, receive, token))).Value.Deliveries.Single();
        await Assert.That(delivery.Event.Data.EventId).IsEqualTo(SubscriptionFilterRf3Protocol.Healthy);
        var processing = new SubscriptionProcessingRequest(Guid.NewGuid(), seed.Group, delivery.Token, SubscriptionFilterRf3Protocol.Handler,
            SubscriptionFilterRf3Protocol.Initial, [new PutDocument(SubscriptionFilterRf3Protocol.Collection, SubscriptionFilterRf3Protocol.Healthy, SubscriptionFilterRf3Protocol.Payload)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await manager.CommitSubscriptionProcessingAsync(processing, token));
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, processing, token))).Value, original);
        var document = await McpCallerAssertions.SdkSuccessAsync(await manager.GetAsync(new(seed.Group.Source.Partition, SubscriptionFilterRf3Protocol.Collection, SubscriptionFilterRf3Protocol.Healthy), token));
        await Assert.That(document!.Json).IsEqualTo(SubscriptionFilterRf3Protocol.Payload);
        await Assert.That(document.Revision).IsEqualTo(SubscriptionFilterRf3Protocol.Initial);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Updated, SubscriptionFilterRf3Protocol.HealthyCheckpoint, false, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await manager.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group), token));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
