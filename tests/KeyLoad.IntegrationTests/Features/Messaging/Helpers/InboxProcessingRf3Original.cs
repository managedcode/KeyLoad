using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Original
{
    internal static async Task<InboxProcessingRf3State> ExecuteAsync(KeyLoadClient client, QueueLaneRef lane,
        MessagingRf3Identity identity, CancellationToken token)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), lane), token));
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var before = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(
            new(lane, InboxProcessingRf3Protocol.Message), token));
        var failed = new ProcessingRequest(Guid.NewGuid(), lane, delivery.Token, InboxProcessingRf3Protocol.Handler,
            InboxProcessingRf3Protocol.Generation,
            [new PutDocument(InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Document,
                InboxProcessingRf3Protocol.Payload, InboxProcessingRf3Protocol.MissingRevision)]);
        await InboxProcessingRf3Assertions.DeniedAsync(await client.CommitProcessingAsync(failed, token), ErrorCode.RevisionConflict);
        await InboxProcessingRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(
            new(lane, InboxProcessingRf3Protocol.Message), token)), before);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(
            new(lane.Partition, InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Document), token))).IsNull();
        var original = failed with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Document,
                InboxProcessingRf3Protocol.Payload, InboxProcessingRf3Protocol.InitialRevision),
             new EnqueueMessage(InboxProcessingRf3Protocol.Output, InboxProcessingRf3Protocol.Message, InboxProcessingRf3Protocol.Payload)]
        };
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await client.CommitProcessingAsync(original, token));
        var input = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(lane, InboxProcessingRf3Protocol.Message), token));
        var output = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(
            new(new(lane.Partition, InboxProcessingRf3Protocol.Output), InboxProcessingRf3Protocol.Message), token));
        var document = await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(
            new(lane.Partition, InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Document), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(InboxProcessingRf3Protocol.Payload);
        await Assert.That(document!.Revision).IsEqualTo(InboxProcessingRf3Protocol.FirstRevision);
        await Assert.That(document.Json).IsEqualTo(InboxProcessingRf3Protocol.Payload);
        return new(lane, identity, original, receipt, input, output, document);
    }
}
