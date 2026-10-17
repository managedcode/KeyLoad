using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Healthy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueLeaseRf3Acknowledged known,
        List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, known.Original.Seed.Worker.Secret, IntegrationClientOptions.Execution());
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            known.Original.Seed.Worker.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueLeaseRf3Future.RequireAsync(sdk, mcp, known.Original.Seed, token);
            await QueueLeaseRf3Assertions.EqualAsync(await QueueLeaseRf3Assertions.InspectAsync(sdk, known.Original.Seed.Lane, token), known.Acked);
            await QueueLeaseRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(
                known.Original.Seed.Enqueue, token)), known.Original.Seed.EnqueueReceipt);
            await QueueLeaseRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(
                known.Healthy, token)), known.HealthyReceipt);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, known.Original.Renew, known.Original.RenewReceipt, token);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, known.Original.Nack, known.Original.NackReceipt, token);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, known.Ack, known.AckReceipt, token);
            var request = new ReceiveRequest(Guid.NewGuid(), known.Original.Seed.Lane);
            var claimed = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
            var delivery = await Assert.That(claimed.Value.Deliveries).HasSingleItem();
            await QueueLeaseRf3Assertions.LiteralDeliveryAsync(delivery, QueueLeaseRf3Protocol.Healthy, QueueLeaseRf3Protocol.HealthyPayload);
            await Assert.That(delivery.Attempt).IsEqualTo(QueueLeaseRf3Protocol.InitialClaim);
            await Assert.That(delivery.LeaseVersion).IsEqualTo((long)QueueLeaseRf3Protocol.InitialClaim);
            await QueueLeaseRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token)), claimed.Value);
            var ack = new DeliveryCommand(Guid.NewGuid(), known.Original.Seed.Lane, delivery.Token, DeliveryAction.Ack);
            var acknowledged = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token));
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, ack, acknowledged.Value, token);
            var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), known.Original.Seed.Lane), token));
            await Assert.That(empty.Deliveries).IsEmpty();
            await QueueLeaseRf3Assertions.EqualAsync(await QueueLeaseRf3Assertions.InspectAsync(sdk, known.Original.Seed.Lane, token), known.Acked);
        }, failures).ConfigureAwait(false);
    }
}
