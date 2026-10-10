using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Continuation
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerRf3Healthy healthy,
        List<Exception> failures, CancellationToken token)
    {
        var seed = healthy.Original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Healthy, token), healthy.Image);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.InspectAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), healthy.Acked);
            await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.GetAsync(
                QueueProducerRf3Assertions.Document(seed, QueueProducerRf3Protocol.Original), token)), healthy.Original.Image.Document);
            await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Healthy, healthy.Image, token);
            await QueueProducerRf3Assertions.ReplayAsync(publisher, mcp, healthy.Command, healthy.Receipt, token);
            await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(healthy.Ack, token)), healthy.AckReceipt);
            await QueueProducerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, healthy.Ack, token))).Value, healthy.AckReceipt);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Original, token), ErrorCode.PermissionDenied, true);
            await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
            var claim = await McpCallerAssertions.SdkSuccessAsync(await publisher.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(QueueProducerRf3Protocol.Healthy);
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueProducerRf3Protocol.HealthyPayload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueProducerRf3Protocol.Headers);
            var ack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
            await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(ack, token)), receipt);
            await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await publisher.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), token))).Deliveries).IsEmpty();
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.InspectAsync(publisher, seed,
                QueueProducerRf3Protocol.Scheduled, token), healthy.Original.Image.Scheduled);
        }, failures).ConfigureAwait(false);
    }
}
