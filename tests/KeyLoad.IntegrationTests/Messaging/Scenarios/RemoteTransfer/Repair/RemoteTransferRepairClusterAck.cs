using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRepairClusterAck
{
    internal static async Task ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferRepairClusterOriginal original, CancellationToken token)
    {
        var seed = original.Seed;
        var receive = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        var delivery = await Assert.That(receive.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(seed.Message.MessageId);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)), receipt);
        await RemoteTransferRepairClusterHealthy.ProveAsync(sdk, mcp, original, acked: true, token);
    }
}
