using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3RenewalBudget
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerRf3Seed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueWorkerDeliveryContext? context = null;
        var exited = QueueWorkerRf3Protocol.NoDeliveries;
        var policy = QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery).Value with
        {
            MaximumConcurrentHandlers = QueueWorkerRf3Protocol.SingleDelivery,
            MaximumRenewalCalls = QueueWorkerRf3Protocol.SingleRenewalBudget,
            RenewalInterval = TimeSpan.FromSeconds(QueueWorkerRf3Protocol.RenewalCadenceSeconds)
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
        await QueueWorkerRf3Claims.LiteralAsync(seed, context.Delivery, token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.RenewalBudget);
        await Assert.That(exited).IsEqualTo(QueueWorkerRf3Protocol.ExpectedJoinedHandlers);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.Operations.Any(row => row.Command?.Action == DeliveryAction.Ack)).IsFalse();
        var renewed = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Renew)).HasSingleItem();
        var receipt = await QueueWorkerRf3Completion.SuccessAsync(renewed);
        await QueueWorkerRf3Assertions.ReceiptAsync(receipt, context.FirstRenewalId, seed,
            [new(nameof(DeliveryAction.Renew), seed.Lane.Queue, QueueWorkerRf3Protocol.First, QueueWorkerRf3Protocol.RenewedStateVersion)]);
        var currentImage = await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Lane, QueueWorkerRf3Protocol.First, token);
        ArgumentNullException.ThrowIfNull(currentImage);
        await Assert.That(currentImage.Metadata.LeaseUntil!.Value).IsGreaterThanOrEqualTo(context.Delivery.LeaseUntil);
        var literal = new MessageInspection(new(QueueWorkerRf3Protocol.First, MessageState.Leased, QueueWorkerRf3Protocol.ClaimAttempt,
            QueueWorkerRf3Protocol.RenewedStateVersion, QueueWorkerRf3Protocol.FirstReadySequence, null, null, seed.Identity.Principal.Id,
            QueueWorkerRf3Protocol.InitialLeaseVersion, currentImage.Metadata.LeaseUntil, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration), QueueWorkerRf3Protocol.Payload, QueueWorkerRf3Protocol.Headers);
        await QueueWorkerRf3Assertions.EqualAsync(currentImage, literal);
        await QueueWorkerRf3RenewalBudgetContinuation.RunAsync(fixture, sdk, mcp, seed, renewed, literal, token);
    }
}
