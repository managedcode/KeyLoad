using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxClusterOriginal
{
    internal static async Task<TargetInboxClusterState> ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, QueueLaneRef source,
        QueueLaneRef target, MessagingClusterIdentity identity, CancellationToken token)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), source), token));
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var request = new CommitInboxRequest(Guid.NewGuid(), target, source, delivery.Id, delivery.DeliveryGeneration,
            TargetInboxClusterProtocol.Handler, TargetInboxClusterProtocol.Generation,
            [new PutDocument(TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Document,
                TargetInboxClusterProtocol.Payload, TargetInboxClusterProtocol.EmptyRevision),
             new EnqueueMessage(TargetInboxClusterProtocol.Output, TargetInboxClusterProtocol.Message, TargetInboxClusterProtocol.Payload)]);
        var before = await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(source, delivery.Id), token));
        var failed = request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Document,
                TargetInboxClusterProtocol.Payload, TargetInboxClusterProtocol.BadRevision)]
        };
        await TargetInboxClusterAssertions.DeniedAsync(await client.CommitInboxAsync(failed, token), ErrorCode.RevisionConflict);
        await TargetInboxClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(source, delivery.Id), token)), before);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(new(target.Partition,
            TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Document), token))).IsNull();
        var result = await TargetInboxClusterCommitOwner.ExecuteAsync(fixture, identity, request, token);
        await Assert.That(result.AlreadyProcessed).IsFalse();
        var input = await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(source, delivery.Id), token));
        await TargetInboxClusterAssertions.EqualAsync(input, before);
        var output = await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(new(target.Partition,
            TargetInboxClusterProtocol.Output), delivery.Id), token));
        var document = await PublicReadAssertions.DocumentAsync(await client.GetAsync(new(target.Partition,
            TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Document), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(TargetInboxClusterProtocol.Payload);
        await Assert.That(document!.Revision).IsEqualTo(TargetInboxClusterProtocol.FirstRevision);
        await Assert.That(document.Json).IsEqualTo(TargetInboxClusterProtocol.Payload);
        var ack = new DeliveryCommand(Guid.NewGuid(), source, delivery.Token, DeliveryAction.Ack);
        var ackReceipt = await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(ack, token));
        input = await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(source, delivery.Id), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Acked);
        return new(source, target, identity, delivery, request, result, ack, ackReceipt, input, output, document);
    }
}
