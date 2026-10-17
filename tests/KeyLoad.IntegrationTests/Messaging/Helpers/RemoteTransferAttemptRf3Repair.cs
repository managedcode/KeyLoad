using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferAttemptRf3Repair
{
    internal static async Task ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, int ceiling, CancellationToken token)
    {
        if (ceiling == RemoteTransferAttemptRf3Protocol.ManualCeiling)
        {
            await RemoteTransferCoordinatorRf3Repair.AcceptAsync(sdk, mcp, seed, intent, token);
            return;
        }
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        var filler = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(filler.Id).IsEqualTo(RemoteTransferCoordinatorRf3Protocol.Filler);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, filler.Token, DeliveryAction.Ack);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)), receipt);
        _ = await RemoteTransferCoordinatorRf3Wait.DeliveredAsync(sdk, seed, token);
    }
}
