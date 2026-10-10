using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Original
{
    internal static async Task<TargetInboxRf3State> ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, QueueLaneRef source,
        QueueLaneRef target, MessagingRf3Identity identity, CancellationToken token)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), source), token));
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var request = new CommitInboxRequest(Guid.NewGuid(), target, source, delivery.Id, delivery.DeliveryGeneration,
            TargetInboxRf3Protocol.Handler, TargetInboxRf3Protocol.Generation,
            [new PutDocument(TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Document,
                TargetInboxRf3Protocol.Payload, TargetInboxRf3Protocol.EmptyRevision),
             new EnqueueMessage(TargetInboxRf3Protocol.Output, TargetInboxRf3Protocol.Message, TargetInboxRf3Protocol.Payload)]);
        var before = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(source, delivery.Id), token));
        var failed = request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Document,
                TargetInboxRf3Protocol.Payload, TargetInboxRf3Protocol.BadRevision)]
        };
        await TargetInboxRf3Assertions.DeniedAsync(await client.CommitInboxAsync(failed, token), ErrorCode.RevisionConflict);
        await TargetInboxRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(source, delivery.Id), token)), before);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(new(target.Partition,
            TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Document), token))).IsNull();
        var result = await TargetInboxRf3CommitOwner.ExecuteAsync(fixture, identity, request, token);
        await Assert.That(result.AlreadyProcessed).IsFalse();
        var input = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(source, delivery.Id), token));
        await TargetInboxRf3Assertions.EqualAsync(input, before);
        var output = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(new(target.Partition,
            TargetInboxRf3Protocol.Output), delivery.Id), token));
        var document = await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(new(target.Partition,
            TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Document), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(TargetInboxRf3Protocol.Payload);
        await Assert.That(document!.Revision).IsEqualTo(TargetInboxRf3Protocol.FirstRevision);
        await Assert.That(document.Json).IsEqualTo(TargetInboxRf3Protocol.Payload);
        var ack = new DeliveryCommand(Guid.NewGuid(), source, delivery.Token, DeliveryAction.Ack);
        var ackReceipt = await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(ack, token));
        input = await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(source, delivery.Id), token));
        await Assert.That(input!.Metadata.State).IsEqualTo(MessageState.Acked);
        return new(source, target, identity, delivery, request, result, ack, ackReceipt, input, output, document);
    }
}
