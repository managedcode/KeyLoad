using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class RemoteTransferCoordinatorClusterRepair
{
    internal static async Task AcceptAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CancellationToken token)
    {
        var receive = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(receive.Deliveries).HasSingleItem();
        var filler = receive.Deliveries.Single();
        await Assert.That(filler.Id).IsEqualTo(RemoteTransferCoordinatorClusterProtocol.Filler);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, filler.Token, DeliveryAction.Ack);
        var original = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)), original);
        var command = seed.Accept(intent);
        var accepted = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(accepted, command, seed.Scenario.DestinationQueue,
            seed.TransferId, RemoteTransferColdProtocol.AcceptKind, accepted.Token.Position);
        await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, command, accepted, token);
        var proof = await PublicReadAssertions.ReceiptAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferCoordinatorClusterProtocol.Missing);
        await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferCoordinatorClusterProtocol.RepairedReady), token);
        var delivered = await RemoteTransferCoordinatorClusterWait.DeliveredAsync(sdk, seed, token);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent with
        {
            State = QueueTransferState.Delivered,
            ReceiptToken = proof.ReceiptToken
        }, token);
        await Assert.That(delivered.ReceiptToken).IsEqualTo(proof.ReceiptToken);
    }
}
