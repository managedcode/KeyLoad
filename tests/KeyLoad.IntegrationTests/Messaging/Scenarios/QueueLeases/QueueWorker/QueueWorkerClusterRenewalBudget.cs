using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterRenewalBudget
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerClusterSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueWorkerDeliveryContext? context = null;
        var exited = QueueWorkerClusterProtocol.NoDeliveries;
        var policy = QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery).Value with
        {
            MaximumConcurrentHandlers = QueueWorkerClusterProtocol.SingleDelivery,
            MaximumRenewalCalls = QueueWorkerClusterProtocol.SingleRenewalBudget,
            RenewalInterval = TimeSpan.FromSeconds(QueueWorkerClusterProtocol.RenewalCadenceSeconds)
        };
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (current, handlerToken) =>
        {
            context = current;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken);
            }
            finally
            {
                Interlocked.Increment(ref exited);
            }
        }, Options.Create(policy), TimeProvider.System, token);
        ArgumentNullException.ThrowIfNull(context);
        await QueueWorkerClusterClaims.LiteralAsync(seed, context.Delivery, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.RenewalBudget);
        await Assert.That(exited).IsEqualTo(QueueWorkerClusterProtocol.ExpectedJoinedHandlers);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.NoAcknowledgements);
        await Assert.That(result.Operations.Any(row => row.Command?.Action == DeliveryAction.Ack)).IsFalse();
        var renewed = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Renew)).HasSingleItem();
        var receipt = await QueueWorkerClusterCompletion.SuccessAsync(renewed);
        await QueueWorkerClusterAssertions.ReceiptAsync(receipt, context.FirstRenewalId, seed,
            [new(nameof(DeliveryAction.Renew), seed.Lane.Queue, QueueWorkerClusterProtocol.First, QueueWorkerClusterProtocol.RenewedStateVersion)]);
        var currentImage = await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Lane, QueueWorkerClusterProtocol.First, token);
        ArgumentNullException.ThrowIfNull(currentImage);
        await Assert.That(currentImage.Metadata.LeaseUntil!.Value).IsGreaterThanOrEqualTo(context.Delivery.LeaseUntil);
        var literal = new MessageInspection(new(QueueWorkerClusterProtocol.First, MessageState.Leased, QueueWorkerClusterProtocol.ClaimAttempt,
            QueueWorkerClusterProtocol.RenewedStateVersion, QueueWorkerClusterProtocol.FirstReadySequence, null, null, seed.Identity.Principal.Id,
            QueueWorkerClusterProtocol.InitialLeaseVersion, currentImage.Metadata.LeaseUntil, DeliveryGeneration: QueueWorkerClusterProtocol.InitialDeliveryGeneration), QueueWorkerClusterProtocol.Payload, QueueWorkerClusterProtocol.Headers);
        await QueueWorkerClusterAssertions.EqualAsync(currentImage, literal);
        await QueueWorkerClusterRenewalBudgetContinuation.RunAsync(fixture, sdk, mcp, seed, renewed, literal, token);
    }
}
