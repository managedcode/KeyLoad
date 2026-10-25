using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLeaseClusterHealthy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueLeaseClusterAcknowledged known,
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
            await QueueLeaseClusterFuture.RequireAsync(sdk, mcp, known.Original.Seed, token);
            await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(sdk, known.Original.Seed.Lane, token), known.Acked);
            await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(
                known.Original.Seed.Enqueue, token)), known.Original.Seed.EnqueueReceipt);
            await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(
                known.Healthy, token)), known.HealthyReceipt);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, known.Original.Renew, known.Original.RenewReceipt, token);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, known.Original.Nack, known.Original.NackReceipt, token);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, known.Ack, known.AckReceipt, token);
            var request = new ReceiveRequest(Guid.NewGuid(), known.Original.Seed.Lane);
            var claimed = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
            var delivery = await Assert.That(claimed.Value.Deliveries).HasSingleItem();
            await QueueLeaseClusterAssertions.LiteralDeliveryAsync(delivery, QueueLeaseClusterProtocol.Healthy, QueueLeaseClusterProtocol.HealthyPayload);
            await Assert.That(delivery.Attempt).IsEqualTo(QueueLeaseClusterProtocol.InitialClaim);
            await Assert.That(delivery.LeaseVersion).IsEqualTo((long)QueueLeaseClusterProtocol.InitialClaim);
            await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token)), claimed.Value);
            var ack = new DeliveryCommand(Guid.NewGuid(), known.Original.Seed.Lane, delivery.Token, DeliveryAction.Ack);
            var acknowledged = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token));
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, ack, acknowledged.Value, token);
            var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), known.Original.Seed.Lane), token));
            await Assert.That(empty.Deliveries).IsEmpty();
            await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(sdk, known.Original.Seed.Lane, token), known.Acked);
        }, failures).ConfigureAwait(false);
    }
}
