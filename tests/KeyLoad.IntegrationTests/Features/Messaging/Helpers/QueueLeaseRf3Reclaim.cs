using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Reclaim
{
    internal static async Task<QueueLeaseRf3Acknowledged?> ExecuteAsync(ClusterFixture fixture,
        QueueLeaseRf3Original original, List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, original.Seed.Worker.Secret, IntegrationClientOptions.Execution());
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, original.Seed.Worker.Secret, token);
        QueueLeaseRf3Acknowledged? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueLeaseRf3Future.RequireAsync(sdk, mcp, original.Seed, token);
            var scheduled = await QueueLeaseRf3Assertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseRf3Assertions.EqualAsync(scheduled, original.Scheduled);
            var due = scheduled.Metadata.NotBefore ?? throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup);
            await DueRecurringRf3Assertions.WaitForDueTimeAsync(due, token).ConfigureAwait(false);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, original.Renew, original.RenewReceipt, token);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, original.Nack, original.NackReceipt, token);
            var request = new ReceiveRequest(Guid.NewGuid(), original.Seed.Lane);
            var claim = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await RequireReclaimedAsync(delivery, original.Delivery);
            var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
            await QueueLeaseRf3Assertions.EqualAsync(replay.Value, claim);
            var leased = await QueueLeaseRf3Assertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseRf3Assertions.LeasedAsync(leased, delivery, original.Seed.Worker.Principal.Id);
            await QueueLeaseRf3Refusal.StaleAsync(sdk, mcp, original, leased, token);
            var ack = new DeliveryCommand(Guid.NewGuid(), original.Seed.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, token));
            await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, ack, receipt, token);
            var acked = await QueueLeaseRf3Assertions.InspectAsync(sdk, original.Seed.Lane, token);
            await QueueLeaseRf3Assertions.EqualAsync(acked, new MessageInspection(leased.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = checked(leased.Metadata.StateVersion + QueueLeaseRf3Protocol.NextClaim),
                LeaseOwner = null,
                LeaseUntil = null
            }, null, null));
            await Assert.That(acked.Metadata.State).IsEqualTo(MessageState.Acked);
            await Assert.That(acked.PayloadJson).IsNull();
            await Assert.That(acked.HeadersJson).IsNull();
            var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), original.Seed.Lane), token));
            await Assert.That(empty.Deliveries).IsEmpty();
            var healthy = new CommandRequest(Guid.NewGuid(), original.Seed.Lane.Partition,
                [new EnqueueMessage(original.Seed.Lane.Queue, QueueLeaseRf3Protocol.Healthy,
                    QueueLeaseRf3Protocol.HealthyPayload, QueueLeaseRf3Protocol.Headers)]);
            var created = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(healthy, token));
            result = new(original, ack, receipt, acked, healthy, created);
        }, failures).ConfigureAwait(false);
        return result;
    }

    private static async Task RequireReclaimedAsync(Delivery actual, Delivery previous)
    {
        await QueueLeaseRf3Assertions.LiteralDeliveryAsync(actual, QueueLeaseRf3Protocol.Message, QueueLeaseRf3Protocol.Payload);
        await Assert.That(actual.Attempt).IsEqualTo(checked(previous.Attempt + QueueLeaseRf3Protocol.NextClaim));
        await Assert.That(actual.LeaseVersion).IsEqualTo(checked(previous.LeaseVersion + QueueLeaseRf3Protocol.NextClaim));
        await Assert.That(actual.DeliveryGeneration).IsEqualTo(previous.DeliveryGeneration);
        await Assert.That(actual.Token == previous.Token).IsFalse();
    }
}
