using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ByteRefusal
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerRf3ByteSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Native.Identity.Secret, IntegrationClientOptions.Execution());
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Native.Identity.Secret, token);
        var options = QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.InvocationDeliveries).Value with
        {
            MaximumInFlightBytes = QueueWorkerRf3ByteProtocol.BudgetBytes,
            MaximumReceiveBytes = QueueWorkerRf3ByteProtocol.BudgetBytes
        };
        var firstReceive = Guid.NewGuid();
        var result = await sdk.RunQueueAckWorkerAsync(seed.Native.Lane, firstReceive, RejectHandlerAsync,
            Options.Create(options), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.InFlightBytes);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerRf3Protocol.NoActiveHandlers);
        await Assert.That(result.PeakInFlightBytes).IsEqualTo((long)QueueWorkerRf3Protocol.NoActiveBytes);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Operations.Any(row => row.Command is not null)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned)).HasSingleItem();
        await Assert.That(original.ReceiveFailure).IsNull();
        await Assert.That(original.Receive).IsEqualTo(new ReceiveRequest(firstReceive, seed.Native.Lane,
            QueueWorkerRf3Protocol.ConcurrentHandlerLimit, QueueWorkerRf3ByteProtocol.BudgetBytes, QueueWorkerRf3Protocol.LeaseSeconds));
        var claim = await Assert.That(original.Claims).HasSingleItem();
        var unhandled = await Assert.That(result.Unacknowledged).HasSingleItem();
        await Assert.That(unhandled).IsEqualTo(claim);
        var delivery = new Delivery(QueueWorkerRf3Protocol.First, seed.Projected, QueueWorkerRf3Protocol.Headers,
            claim.Token, QueueWorkerRf3Protocol.InitialLeaseVersion, claim.LeaseUntil,
            QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.InitialDeliveryGeneration);
        await Assert.That(claim).IsEqualTo(new QueueWorkerClaimIdentity(QueueWorkerRf3Protocol.First, delivery.Token,
            QueueWorkerRf3Protocol.InitialLeaseVersion, delivery.LeaseUntil, QueueWorkerRf3Protocol.ClaimAttempt,
            QueueWorkerRf3Protocol.InitialDeliveryGeneration));
        await QueueWorkerRf3ByteImages.DeliveryAsync(seed, delivery, token);
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerRf3ByteReplay.ReceiveAsync(admin, sdk, mcp, seed, original, delivery, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await QueueWorkerRf3ByteReplay.ReceiveAsync(admin, sdk, mcp, seed, original, delivery, token);
        await QueueWorkerRf3ByteContinuation.RunAsync(fixture, admin, sdk, mcp, seed, delivery, token);
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerRf3Protocol.Missing);
}
