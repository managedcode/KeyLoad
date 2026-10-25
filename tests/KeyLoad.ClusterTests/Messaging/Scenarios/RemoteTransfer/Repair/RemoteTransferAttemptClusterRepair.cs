using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class RemoteTransferAttemptClusterRepair
{
    internal static async Task ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, int ceiling, CancellationToken token)
    {
        if (ceiling == RemoteTransferAttemptClusterProtocol.ManualCeiling)
        {
            await RemoteTransferCoordinatorClusterRepair.AcceptAsync(sdk, mcp, seed, intent, token);
            return;
        }
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        var filler = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(filler.Id).IsEqualTo(RemoteTransferCoordinatorClusterProtocol.Filler);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, filler.Token, DeliveryAction.Ack);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)), receipt);
        _ = await RemoteTransferCoordinatorClusterWait.DeliveredAsync(sdk, seed, token);
    }
}
