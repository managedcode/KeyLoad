using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Repair
{
    internal static async Task AcceptAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CancellationToken token)
    {
        var receive = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(receive.Deliveries).HasSingleItem();
        var filler = receive.Deliveries.Single();
        await Assert.That(filler.Id).IsEqualTo(RemoteTransferCoordinatorRf3Protocol.Filler);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, filler.Token, DeliveryAction.Ack);
        var original = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)), original);
        _ = await RemoteTransferColdStages.AcceptAsync(sdk, mcp, seed, intent, seed.Accept(intent),
            RemoteTransferCoordinatorRf3Protocol.RepairedReady, token);
    }
}
