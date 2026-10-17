using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3HandlerFailure
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerRf3Seed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        var original = new InvalidOperationException(QueueWorkerRf3Protocol.HandlerFailure);
        QueueWorkerDeliveryContext? held = null;
        var joined = false;
        var receiveId = Guid.NewGuid();
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, receiveId, async (context, handlerToken) =>
        {
            held = context;
            try
            {
                await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, context,
                    QueueWorkerRf3Protocol.FirstReadySequence, handlerToken);
                throw original;
            }
            finally
            {
                joined = true;
            }
        }, QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery), TimeProvider.System, token);
        ArgumentNullException.ThrowIfNull(held);
        await Assert.That(joined).IsTrue();
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.HandlerFailed);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.Failures.Any(value => ReferenceEquals(value, original))).IsTrue();
        await QueueWorkerRf3Assertions.EqualAsync(result.Unacknowledged.ToArray(),
            [new QueueWorkerClaimIdentity(held.Delivery.Id, held.Delivery.Token, held.Delivery.LeaseVersion,
                held.Delivery.LeaseUntil, held.Delivery.Attempt, held.Delivery.DeliveryGeneration)]);
        await Assert.That(result.Operations.Any(value => value.Command is not null)).IsFalse();
        var receive = await Assert.That(result.Operations.Where(value => value.Stage == QueueWorkerOperationStage.Returned
            && value.Receive?.RequestId == receiveId)).HasSingleItem();
        await Assert.That(receive.ReceiveFailure).IsNull();
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, held, QueueWorkerRf3Protocol.FirstReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Second,
            QueueWorkerRf3Protocol.SecondReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Third,
            QueueWorkerRf3Protocol.ThirdReadySequence, token);
        await QueueWorkerRf3RenewalContinuation.RunAsync(fixture, sdk, mcp, seed, held, token);
    }
}
