using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.Messaging;

internal static class InboxProcessingClusterOriginal
{
    internal static async Task<InboxProcessingClusterState> ExecuteAsync(KeyLoadClient client, QueueLaneRef lane,
        MessagingClusterIdentity identity, CancellationToken token)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), lane), token));
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var before = await PublicReadAssertions.MessageAsync(await client.InspectAsync(
            new(lane, InboxProcessingClusterProtocol.Message), token));
        var failed = new ProcessingRequest(Guid.NewGuid(), lane, delivery.Token, InboxProcessingClusterProtocol.Handler,
            InboxProcessingClusterProtocol.Generation,
            [new PutDocument(InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Document,
                InboxProcessingClusterProtocol.Payload, InboxProcessingClusterProtocol.MissingRevision)]);
        await InboxProcessingClusterAssertions.DeniedAsync(await client.CommitProcessingAsync(failed, token), ErrorCode.RevisionConflict);
        await InboxProcessingClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(
            new(lane, InboxProcessingClusterProtocol.Message), token)), before);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(
            new(lane.Partition, InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Document), token))).IsNull();
        var original = failed with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Document,
                InboxProcessingClusterProtocol.Payload, InboxProcessingClusterProtocol.InitialRevision),
             new EnqueueMessage(InboxProcessingClusterProtocol.Output, InboxProcessingClusterProtocol.Message, InboxProcessingClusterProtocol.Payload)]
        };
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await client.CommitProcessingAsync(original, token));
        var input = await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(lane, InboxProcessingClusterProtocol.Message), token));
        var output = await PublicReadAssertions.MessageAsync(await client.InspectAsync(
            new(new(lane.Partition, InboxProcessingClusterProtocol.Output), InboxProcessingClusterProtocol.Message), token));
        var document = await PublicReadAssertions.DocumentAsync(await client.GetAsync(
            new(lane.Partition, InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Document), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(InboxProcessingClusterProtocol.Payload);
        await Assert.That(document!.Revision).IsEqualTo(InboxProcessingClusterProtocol.FirstRevision);
        await Assert.That(document.Json).IsEqualTo(InboxProcessingClusterProtocol.Payload);
        return new(lane, identity, original, receipt, input, output, document);
    }
}
