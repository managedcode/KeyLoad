using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterHealthyFlow
{
    internal static async Task<QueueProducerClusterHealthy?> ExecuteAsync(ClusterFixture fixture, QueueProducerClusterOriginal original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueProducerClusterHealthy? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            var request = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
            var claim = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(QueueProducerClusterProtocol.Original);
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueProducerClusterProtocol.Payload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueProducerClusterProtocol.Headers);
            var leased = await QueueProducerClusterAssertions.InspectAsync(publisher, seed, QueueProducerClusterProtocol.Original, token);
            var ack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Ack);
            var ackReceipt = await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(ack, token));
            await QueueProducerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, ack, token))).Value, ackReceipt);
            var acked = await QueueProducerClusterAssertions.InspectAsync(publisher, seed, QueueProducerClusterProtocol.Original, token);
            await QueueProducerClusterAssertions.EqualAsync(acked, leased with
            {
                Metadata = leased.Metadata with
                {
                    State = MessageState.Acked,
                    StateVersion = checked(leased.Metadata.StateVersion + QueueProducerClusterProtocol.StateVersionAdvance),
                    LeaseOwner = null,
                    LeaseUntil = null
                },
                PayloadJson = null,
                HeadersJson = null
            });
            var healthy = QueueProducerClusterSetup.Fresh(seed, QueueProducerClusterProtocol.Healthy, QueueProducerClusterProtocol.Healthy);
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy, token))).Value;
            var image = await QueueProducerClusterAssertions.ImageAsync(publisher, seed, QueueProducerClusterProtocol.Healthy, token);
            await QueueProducerClusterObservedReplay.RunAsync(publisher, mcp, seed, healthy, receipt,
                QueueProducerClusterProtocol.Healthy, image, token);
            await AtomicProducerClusterEventAssertions.LiteralAsync(image.Events, seed, QueueProducerClusterProtocol.Healthy,
                QueueProducerClusterProtocol.HealthyPayload, QueueProducerClusterProtocol.HealthyEventSequence);
            await QueueProducerClusterLiteralAssertions.ProducerAsync(fixture, seed, healthy, receipt, image,
                QueueProducerClusterProtocol.Healthy, QueueProducerClusterProtocol.HealthyPayload, QueueProducerClusterProtocol.HealthyReadySequence);
            await QueueProducerClusterAssertions.EqualAsync(image.Scheduled, original.Image.Scheduled);
            await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, QueueProducerClusterProtocol.Healthy, image, token);
            result = new(original, healthy, receipt, ack, ackReceipt, acked, image);
        }, failures).ConfigureAwait(false);
        return result;
    }
}
