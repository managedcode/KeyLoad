using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueLeaseClusterReclaim
{
    internal static async Task<QueueLeaseClusterAcknowledged?> ExecuteAsync(ClusterFixture fixture,
        QueueLeaseClusterOriginal original, List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, original.Seed.Worker.Secret, IntegrationClientOptions.Execution());
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, original.Seed.Worker.Secret, token);
        QueueLeaseClusterAcknowledged? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueLeaseClusterFuture.RequireAsync(sdk, mcp, original.Seed, token);
            var scheduled = await QueueLeaseClusterAssertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseClusterAssertions.EqualAsync(scheduled, original.Scheduled);
            var due = scheduled.Metadata.NotBefore ?? throw new InvalidOperationException(QueueLeaseClusterProtocol.MissingSetup);
            await DueRecurringClusterAssertions.WaitForDueTimeAsync(due, token).ConfigureAwait(false);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, original.Renew, original.RenewReceipt, token);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, original.Nack, original.NackReceipt, token);
            var request = new ReceiveRequest(Guid.NewGuid(), original.Seed.Lane);
            var claim = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await RequireReclaimedAsync(delivery, original.Delivery);
            var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
            await QueueLeaseClusterAssertions.EqualAsync(replay.Value, claim);
            var leased = await QueueLeaseClusterAssertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseClusterAssertions.LeasedAsync(leased, delivery, original.Seed.Worker.Principal.Id);
            await QueueLeaseClusterRefusal.StaleAsync(sdk, mcp, original, leased, token);
            var ack = new DeliveryCommand(Guid.NewGuid(), original.Seed.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token));
            await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, ack, receipt, token);
            var acked = await QueueLeaseClusterAssertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseClusterAssertions.EqualAsync(acked, new MessageInspection(leased.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = checked(leased.Metadata.StateVersion + QueueLeaseClusterProtocol.NextClaim),
                LeaseOwner = null,
                LeaseUntil = null
            }, null, null));
            await Assert.That(acked.Metadata.State).IsEqualTo(MessageState.Acked);
            await Assert.That(acked.PayloadJson).IsNull();
            await Assert.That(acked.HeadersJson).IsNull();
            var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), original.Seed.Lane), token));
            await Assert.That(empty.Deliveries).IsEmpty();
            var healthy = new CommandRequest(Guid.NewGuid(), original.Seed.Lane.Partition,
                [new EnqueueMessage(original.Seed.Lane.Queue, QueueLeaseClusterProtocol.Healthy,
                    QueueLeaseClusterProtocol.HealthyPayload, QueueLeaseClusterProtocol.Headers)]);
            var created = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(healthy, token));
            result = new(original, ack, receipt, acked, healthy, created);
        }, failures).ConfigureAwait(false);
        return result;
    }

    private static async Task RequireReclaimedAsync(Delivery actual, Delivery previous)
    {
        await QueueLeaseClusterAssertions.LiteralDeliveryAsync(actual, QueueLeaseClusterProtocol.Message, QueueLeaseClusterProtocol.Payload);
        await Assert.That(actual.Attempt).IsEqualTo(checked(previous.Attempt + QueueLeaseClusterProtocol.NextClaim));
        await Assert.That(actual.LeaseVersion).IsEqualTo(checked(previous.LeaseVersion + QueueLeaseClusterProtocol.NextClaim));
        await Assert.That(actual.DeliveryGeneration).IsEqualTo(previous.DeliveryGeneration);
        await Assert.That(actual.Token == previous.Token).IsFalse();
    }
}
