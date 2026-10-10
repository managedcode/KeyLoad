using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3OriginalFlow
{
    internal static async Task<SubscriptionFilterRf3Original> RunAsync(ClusterFixture fixture,
        SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var first = new KeyLoadClient(http, seed.First.Secret, IntegrationClientOptions.Execution());
        var manager = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        await using var second = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, seed.Second.Secret, token);
        await using var official = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
        var a = new ReceiveSubscriptionRequest(Guid.NewGuid(), seed.Group);
        var b = new ReceiveSubscriptionRequest(Guid.NewGuid(), seed.Group, SubscriptionFilterRf3Protocol.Window);
        var ar = await McpCallerAssertions.SdkSuccessAsync(await first.ReceiveSubscriptionAsync(a, token));
        var br = (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(await second.CallAsync(McpCallerTools.SubscriptionsReceive, b, token))).Value;
        await Assert.That(ar.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(new long[] { 1 }, CollectionOrdering.Matching);
        await Assert.That(br.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(new long[] { 2, 3 }, CollectionOrdering.Matching);
        var foreign = new SubscriptionDeliveryCommand(Guid.NewGuid(), seed.Group, ar.Deliveries.Single().Token, DeliveryAction.Ack);
        await McpCallerAssertions.ErrorAsync(await second.CallAsync(McpCallerTools.SubscriptionsComplete, foreign, token), ErrorCode.TokenInvalidated, true);
        var ackFirst = foreign with { CommandId = Guid.NewGuid() };
        var receiptFirst = await McpCallerAssertions.SdkSuccessAsync(await first.CompleteSubscriptionAsync(ackFirst, token));
        var ackThird = new SubscriptionDeliveryCommand(Guid.NewGuid(), seed.Group, br.Deliveries.Last().Token, DeliveryAction.Ack);
        var receiptThird = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await second.CallAsync(McpCallerTools.SubscriptionsComplete, ackThird, token))).Value;
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, official, seed.Group, SubscriptionFilterRf3Protocol.Initial, SubscriptionFilterRf3Protocol.Gap, false, token);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, official, seed.Independent, SubscriptionFilterRf3Protocol.Initial, 0, false, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await first.ReceiveSubscriptionAsync(new(Guid.NewGuid(), seed.Group), token));
        await Assert.That(empty.Deliveries).IsEmpty();
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, official, seed.Replacement, ErrorCode.Conflict, token);
        var workerDenied = await first.ConfigureSubscriptionAsync(seed.Replacement with { CommandId = Guid.NewGuid() }, token);
        await Assert.That(workerDenied.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.SdkSuccessAsync(await manager.SetSubscriptionPausedAsync(new(Guid.NewGuid(), seed.Group, SubscriptionFilterRf3Protocol.Initial, true), token));
        var history = await McpCallerAssertions.SdkSuccessAsync(await manager.ReadEventSourceAsync(new(seed.Group.Source), token));
        await Assert.That(history.Events.Select(x => x.Position)).IsEquivalentTo(new long[] { 1, 2, 3, 4 }, CollectionOrdering.Matching);
        return new(seed, a, ar, b, br, ackFirst, receiptFirst, ackThird, receiptThird, history);
    }
}
