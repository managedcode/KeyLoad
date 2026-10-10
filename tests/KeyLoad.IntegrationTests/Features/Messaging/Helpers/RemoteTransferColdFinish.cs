using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdFinish
{
    internal static async Task ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferInspection intent, QueueTransferReceiptInspection proof,
        CancellationToken token)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(seed.Message.MessageId);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Scenario.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var acknowledged = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token))).Value;
        await SqlRf3Protocol.EqualAsync(acknowledged, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token)));
        var image = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(seed.Scenario.DestinationQueue, seed.Message.MessageId), token));
        ArgumentNullException.ThrowIfNull(image);
        await Assert.That(image.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(image.PayloadJson).IsNull();
        await Assert.That(image.HeadersJson).IsNull();
        var duplicate = seed.Accept(intent);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(duplicate, token));
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, duplicate, receipt, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, image, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), seed.Scenario.DestinationQueue), token));
        await Assert.That(empty.Deliveries).IsEmpty();
        await HealthyAsync(sdk, mcp, seed, token);
    }

    private static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        CancellationToken token)
    {
        var transferId = Guid.NewGuid();
        var message = seed.Message with
        {
            MessageId = RemoteTransferColdProtocol.Healthy,
            PayloadJson = RemoteTransferColdProtocol.HealthyPayload
        };
        var create = new CommandRequest(Guid.NewGuid(), seed.Scenario.SourcePartition,
            [new CreateQueueTransfer(seed.Scenario.SourceQueue, transferId, seed.Scenario.DestinationQueue, message)]);
        var healthy = seed with { TransferId = transferId, Message = message, Create = create };
        var original = await RemoteTransferColdStages.CreateAsync(sdk, mcp, healthy, token);
        var command = healthy.Accept(original.Intent);
        var accepted = await RemoteTransferColdStages.AcceptAsync(sdk, mcp, healthy, original.Intent, command, RemoteTransferColdProtocol.HealthyReadySequence, token);
        await RemoteTransferColdStages.CompleteAsync(sdk, mcp, healthy, original.Intent, accepted.Proof,
            healthy.Complete(accepted.Proof), token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, healthy,
            RemoteTransferColdAssertions.Ready(healthy, RemoteTransferColdProtocol.HealthyReadySequence), token);
    }
}
