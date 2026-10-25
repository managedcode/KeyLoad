using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterContinuation
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerClusterHealthy healthy,
        List<Exception> failures, CancellationToken token)
    {
        var seed = healthy.Original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Healthy, token), healthy.Image);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.InspectAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), healthy.Acked);
            await QueueProducerClusterAssertions.EqualAsync(await PublicReadAssertions.DocumentAsync(await publisher.GetAsync(
                QueueProducerClusterAssertions.Document(seed, QueueProducerClusterProtocol.Original), token)), healthy.Original.Image.Document);
            await AtomicProducerClusterEventAssertions.PublicAsync(mcp, seed, QueueProducerClusterProtocol.Original, healthy.Original.Image.Events, token);
            await QueueProducerClusterAssertions.EqualAsync(await AtomicProducerClusterEventAssertions.ReadAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), healthy.Original.Image.Events);
            await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, QueueProducerClusterProtocol.Healthy, healthy.Image, token);
            await QueueProducerClusterObservedReplay.RunAsync(publisher, mcp, seed, healthy.Command, healthy.Receipt,
                QueueProducerClusterProtocol.Healthy, healthy.Image, token);
            await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(healthy.Ack, token)), healthy.AckReceipt);
            await QueueProducerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, healthy.Ack, token))).Value, healthy.AckReceipt);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Original, token), ErrorCode.PermissionDenied, true)).Value;
            await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
            var claim = await McpCallerAssertions.SdkSuccessAsync(await publisher.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(QueueProducerClusterProtocol.Healthy);
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueProducerClusterProtocol.HealthyPayload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueProducerClusterProtocol.Headers);
            var ack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
            await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.CompleteAsync(ack, token)), receipt);
            await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await publisher.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), token))).Deliveries).IsEmpty();
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.InspectAsync(publisher, seed,
                QueueProducerClusterProtocol.Scheduled, token), healthy.Original.Image.Scheduled);
        }, failures).ConfigureAwait(false);
    }
}
