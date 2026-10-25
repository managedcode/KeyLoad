using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLeaseClusterRenew
{
    internal static async Task<QueueLeaseClusterOriginal?> ExecuteAsync(ClusterFixture fixture,
        QueueLeaseClusterSeed seed, List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Worker.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Worker.Secret, token);
        QueueLeaseClusterOriginal? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueLeaseClusterFuture.RequireAsync(sdk, mcp, seed, token);
            var request = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
            var claim = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
            var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
            await QueueLeaseClusterAssertions.LiteralDeliveryAsync(delivery, QueueLeaseClusterProtocol.Message, QueueLeaseClusterProtocol.Payload);
            await Assert.That(delivery.Attempt).IsEqualTo(QueueLeaseClusterProtocol.InitialClaim);
            await Assert.That(delivery.LeaseVersion).IsEqualTo((long)QueueLeaseClusterProtocol.InitialClaim);
            var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
                McpCallerTools.MessagesReceive, request, token));
            await QueueLeaseClusterAssertions.EqualAsync(replay.Value, claim);
            var before = await QueueLeaseClusterAssertions.InspectAsync(sdk, seed.Lane, token);
            await QueueLeaseClusterAssertions.LeasedAsync(before, delivery, seed.Worker.Principal.Id);
            var renew = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Renew);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(renew, token));
            await Assert.That(receipt.CommandId).IsEqualTo(renew.CommandId);
            var renewed = await QueueLeaseClusterAssertions.InspectAsync(sdk, seed.Lane, token);
            await RequireRenewedAsync(renewed, before, receipt);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, renew, receipt, token);
            var invalid = renew with
            {
                CommandId = Guid.NewGuid(),
                LeaseSeconds = checked(seed.Resource.QueuePolicy.MaxLeaseSeconds + QueueLeaseClusterProtocol.NextClaim)
            };
            await QueueLeaseClusterAssertions.DeniedAsync(await sdk.CompleteAsync(invalid, token), ErrorCode.Validation);
            await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(sdk, seed.Lane, token), renewed);
            await QueueLeaseClusterRefusal.ForeignAsync(fixture, seed, delivery, sdk, renewed, failures, token);
            ServerFailureObserver.ThrowIfAny(failures);
            var nack = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Nack);
            var nacked = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, nack, token));
            var scheduled = await QueueLeaseClusterAssertions.InspectAsync(sdk, seed.Lane, token);
            await Assert.That(scheduled.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Assert.That(scheduled.Metadata.LeaseOwner).IsNull();
            await Assert.That(scheduled.Metadata.LeaseUntil).IsNull();
            await Assert.That(scheduled.PayloadJson).IsEqualTo(QueueLeaseClusterProtocol.Payload);
            await Assert.That(scheduled.HeadersJson).IsEqualTo(QueueLeaseClusterProtocol.Headers);
            await QueueLeaseClusterAssertions.ReplayedAsync(sdk, mcp, nack, nacked.Value, token);
            original = new(seed, request, claim, delivery, renew, receipt, nack, nacked.Value, scheduled);
        }, failures).ConfigureAwait(false);
        return original;
    }

    private static async Task RequireRenewedAsync(MessageInspection actual, MessageInspection before, CommitReceipt receipt)
    {
        await Assert.That(actual.Metadata.LeaseUntil).IsNotNull();
        await Assert.That(actual.Metadata.LeaseUntil!.Value).IsGreaterThanOrEqualTo(before.Metadata.LeaseUntil!.Value);
        await QueueLeaseClusterAssertions.EqualAsync(actual, before with
        {
            Metadata = before.Metadata with
            {
                LeaseUntil = actual.Metadata.LeaseUntil,
                StateVersion = checked(before.Metadata.StateVersion + QueueLeaseClusterProtocol.NextClaim)
            }
        });
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(mutation.Revision).IsEqualTo(actual.Metadata.StateVersion);
    }
}
