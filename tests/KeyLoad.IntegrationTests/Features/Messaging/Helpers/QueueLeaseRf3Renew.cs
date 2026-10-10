using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Renew
{
    internal static async Task<QueueLeaseRf3Original?> ExecuteAsync(ClusterFixture fixture,
        QueueLeaseRf3Seed seed, List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Worker.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Worker.Secret, token);
        QueueLeaseRf3Original? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueLeaseRf3Future.RequireAsync(sdk, mcp, seed, token);
            var request = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
            var claim = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await QueueLeaseRf3Assertions.LiteralDeliveryAsync(delivery, QueueLeaseRf3Protocol.Message, QueueLeaseRf3Protocol.Payload);
            await Assert.That(delivery.Attempt).IsEqualTo(QueueLeaseRf3Protocol.InitialClaim);
            await Assert.That(delivery.LeaseVersion).IsEqualTo((long)QueueLeaseRf3Protocol.InitialClaim);
            var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
                McpCallerTools.MessagesReceive, request, token));
            await QueueLeaseRf3Assertions.EqualAsync(replay.Value, claim);
            var before = await QueueLeaseRf3Assertions.InspectAsync(sdk, seed.Lane, token);
            await QueueLeaseRf3Assertions.LeasedAsync(before, delivery, seed.Worker.Principal.Id);
            var renew = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Renew);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(renew, token));
            await Assert.That(receipt.CommandId).IsEqualTo(renew.CommandId);
            var renewed = await QueueLeaseRf3Assertions.InspectAsync(sdk, seed.Lane, token);
            await RequireRenewedAsync(renewed, before, receipt);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, renew, receipt, token);
            var invalid = renew with { CommandId = Guid.NewGuid(), LeaseSeconds = checked(seed.Resource.QueuePolicy.MaxLeaseSeconds + QueueLeaseRf3Protocol.NextClaim) };
            await QueueLeaseRf3Assertions.DeniedAsync(await sdk.CompleteAsync(invalid, token), ErrorCode.Validation);
            await QueueLeaseRf3Assertions.EqualAsync(await QueueLeaseRf3Assertions.InspectAsync(sdk, seed.Lane, token), renewed);
            await QueueLeaseRf3Refusal.ForeignAsync(fixture, seed, delivery, sdk, renewed, failures, token);
            ServerFailureObserver.ThrowIfAny(failures);
            var nack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Nack);
            var nacked = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, nack, token));
            var scheduled = await QueueLeaseRf3Assertions.InspectAsync(sdk, seed.Lane, token);
            await Assert.That(scheduled.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Assert.That(scheduled.Metadata.LeaseOwner).IsNull();
            await Assert.That(scheduled.Metadata.LeaseUntil).IsNull();
            await Assert.That(scheduled.PayloadJson).IsEqualTo(QueueLeaseRf3Protocol.Payload);
            await Assert.That(scheduled.HeadersJson).IsEqualTo(QueueLeaseRf3Protocol.Headers);
            await QueueLeaseRf3Assertions.ReplayedAsync(sdk, mcp, nack, nacked.Value, token);
            original = new(seed, request, claim, delivery, renew, receipt, nack, nacked.Value, scheduled);
        }, failures).ConfigureAwait(false);
        return original;
    }

    private static async Task RequireRenewedAsync(MessageInspection actual, MessageInspection before, CommitReceipt receipt)
    {
        await Assert.That(actual.Metadata.LeaseUntil).IsNotNull();
        await Assert.That(actual.Metadata.LeaseUntil!.Value).IsGreaterThanOrEqualTo(before.Metadata.LeaseUntil!.Value);
        await QueueLeaseRf3Assertions.EqualAsync(actual, before with
        {
            Metadata = before.Metadata with
            { LeaseUntil = actual.Metadata.LeaseUntil, StateVersion = checked(before.Metadata.StateVersion + QueueLeaseRf3Protocol.NextClaim) }
        });
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(mutation.Revision).IsEqualTo(actual.Metadata.StateVersion);
    }
}
