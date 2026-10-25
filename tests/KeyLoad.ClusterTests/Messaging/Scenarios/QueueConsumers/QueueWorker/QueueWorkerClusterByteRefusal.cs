using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterByteRefusal
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerClusterByteSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Execution.Identity.Secret, IntegrationClientOptions.Execution());
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Execution.Identity.Secret, token);
        var options = QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.InvocationDeliveries).Value with
        {
            MaximumInFlightBytes = QueueWorkerClusterByteProtocol.BudgetBytes,
            MaximumReceiveBytes = QueueWorkerClusterByteProtocol.BudgetBytes
        };
        var firstReceive = Guid.NewGuid();
        var result = await sdk.RunQueueAckWorkerAsync(seed.Execution.Lane, firstReceive, RejectHandlerAsync,
            Options.Create(options), TimeProvider.System, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.InFlightBytes);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerClusterProtocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.NoAcknowledgements);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerClusterProtocol.NoActiveHandlers);
        await Assert.That(result.PeakInFlightBytes).IsEqualTo((long)QueueWorkerClusterProtocol.NoActiveBytes);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Operations.Any(row => row.Command is not null)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned)).HasSingleItem();
        await Assert.That(original.ReceiveFailure).IsNull();
        await Assert.That(original.Receive).IsEqualTo(new ReceiveRequest(firstReceive, seed.Execution.Lane,
            QueueWorkerClusterProtocol.ConcurrentHandlerLimit, QueueWorkerClusterByteProtocol.BudgetBytes, QueueWorkerClusterProtocol.LeaseSeconds));
        var claim = await Assert.That(original.Claims).HasSingleItem();
        var unhandled = await Assert.That(result.Unacknowledged).HasSingleItem();
        await Assert.That(unhandled).IsEqualTo(claim);
        var delivery = new Delivery(QueueWorkerClusterProtocol.First, seed.Projected, QueueWorkerClusterProtocol.Headers,
            claim.Token, QueueWorkerClusterProtocol.InitialLeaseVersion, claim.LeaseUntil,
            QueueWorkerClusterProtocol.ClaimAttempt, QueueWorkerClusterProtocol.InitialDeliveryGeneration);
        await Assert.That(claim).IsEqualTo(new QueueWorkerClaimIdentity(QueueWorkerClusterProtocol.First, delivery.Token,
            QueueWorkerClusterProtocol.InitialLeaseVersion, delivery.LeaseUntil, QueueWorkerClusterProtocol.ClaimAttempt,
            QueueWorkerClusterProtocol.InitialDeliveryGeneration));
        await QueueWorkerClusterByteImages.DeliveryAsync(seed, delivery, token);
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerClusterByteReplay.ReceiveAsync(admin, sdk, mcp, seed, original, delivery, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await QueueWorkerClusterByteReplay.ReceiveAsync(admin, sdk, mcp, seed, original, delivery, token);
        await QueueWorkerClusterByteContinuation.RunAsync(fixture, admin, sdk, mcp, seed, delivery, token);
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
}
