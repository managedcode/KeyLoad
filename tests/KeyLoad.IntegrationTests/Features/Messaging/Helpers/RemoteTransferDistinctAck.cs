using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctAck
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, RemoteTransferDistinctSeed setup,
        QueueTransferInspection intent, CommandRequest accept,
        (CommitReceipt Receipt, QueueTransferReceiptInspection Proof) target, CancellationToken token)
    {
        var seed = setup.Transfer;
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var sdk = new KeyLoadClient(http, setup.Technical.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node4,
            setup.Technical.Secret, token);
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(seed.Message.MessageId);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token))).Value;
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)));
        var expected = new MessageInspection(new(seed.Message.MessageId, MessageState.Acked,
            RemoteTransferDistinctAckProtocol.Attempts, RemoteTransferDistinctAckProtocol.StateVersion,
            RemoteTransferColdProtocol.OriginalReadySequence, null, null,
            LeaseVersion: RemoteTransferDistinctAckProtocol.LeaseVersion), null, null);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(seed.Scenario.DestinationQueue, seed.Message.MessageId), token)));
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (source, official) =>
        {
            await QueueProducerRf3Assertions.ReplayAsync(source, official, accept, target.Receipt, token);
            await RemoteTransferColdAssertions.ReceiptAsync(source, official, seed, target.Proof, token);
            await RemoteTransferColdAssertions.MessageAsync(source, official, seed, expected, token);
            await RemoteTransferColdAssertions.SourceAsync(source, official, seed, intent with
            { State = QueueTransferState.Delivered, ReceiptToken = target.Proof.ReceiptToken }, token);
        }, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
