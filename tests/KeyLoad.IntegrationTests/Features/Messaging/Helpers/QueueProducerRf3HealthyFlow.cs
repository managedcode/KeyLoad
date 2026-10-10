using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3HealthyFlow
{
    internal static async Task<QueueProducerRf3Healthy?> ExecuteAsync(ClusterFixture fixture, QueueProducerRf3Original original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueProducerRf3Healthy? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            var request = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
            var claim = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(QueueProducerRf3Protocol.Original);
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueProducerRf3Protocol.Payload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueProducerRf3Protocol.Headers);
            var leased = await QueueProducerRf3Assertions.InspectAsync(publisher, seed, QueueProducerRf3Protocol.Original, token);
            var ack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Ack);
            var ackReceipt = await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(ack, token));
            await QueueProducerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack, token))).Value, ackReceipt);
            var acked = await QueueProducerRf3Assertions.InspectAsync(publisher, seed, QueueProducerRf3Protocol.Original, token);
            await QueueProducerRf3Assertions.EqualAsync(acked, leased with
            {
                Metadata = leased.Metadata with
                {
                    State = MessageState.Acked,
                    StateVersion = checked(leased.Metadata.StateVersion + QueueProducerRf3Protocol.StateVersionAdvance),
                    LeaseOwner = null,
                    LeaseUntil = null
                },
                PayloadJson = null,
                HeadersJson = null
            });
            var healthy = QueueProducerRf3Setup.Fresh(seed, QueueProducerRf3Protocol.Healthy, QueueProducerRf3Protocol.Healthy);
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy, token))).Value;
            await QueueProducerRf3Assertions.ReplayAsync(publisher, mcp, healthy, receipt, token);
            var image = await QueueProducerRf3Assertions.ImageAsync(publisher, seed, QueueProducerRf3Protocol.Healthy, token);
            await AtomicProducerRf3EventAssertions.LiteralAsync(image.Events, seed, QueueProducerRf3Protocol.Healthy,
                QueueProducerRf3Protocol.HealthyPayload, QueueProducerRf3Protocol.HealthyEventSequence);
            await Assert.That(image.Document.Json).IsEqualTo(QueueProducerRf3Protocol.HealthyPayload);
            await Assert.That(image.Ready.Metadata.ReadySequence).IsEqualTo(QueueProducerRf3Protocol.HealthyReadySequence);
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(image.Ready.PayloadJson).IsEqualTo(QueueProducerRf3Protocol.HealthyPayload);
            await Assert.That(image.Ready.HeadersJson).IsEqualTo(QueueProducerRf3Protocol.Headers);
            await QueueProducerRf3Assertions.EqualAsync(image.Scheduled, original.Image.Scheduled);
            await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Healthy, image, token);
            result = new(original, healthy, receipt, ack, ackReceipt, acked, image);
        }, failures).ConfigureAwait(false);
        return result;
    }
}
