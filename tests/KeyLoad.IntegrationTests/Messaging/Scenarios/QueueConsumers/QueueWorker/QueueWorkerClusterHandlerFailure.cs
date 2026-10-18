using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterHandlerFailure
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerClusterSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        var original = new InvalidOperationException(QueueWorkerClusterProtocol.HandlerFailure);
        QueueWorkerDeliveryContext? held = null;
        var joined = false;
        var receiveId = Guid.NewGuid();
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, receiveId, async (context, handlerToken) =>
        {
            held = context;
            try
            {
                await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, context,
                    QueueWorkerClusterProtocol.FirstReadySequence, handlerToken);
                throw original;
            }
            finally
            {
                joined = true;
            }
        }, QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery), TimeProvider.System, token);
        ArgumentNullException.ThrowIfNull(held);
        await Assert.That(joined).IsTrue();
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.HandlerFailed);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerClusterProtocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.NoAcknowledgements);
        await Assert.That(result.Failures.Any(value => ReferenceEquals(value, original))).IsTrue();
        await QueueWorkerClusterAssertions.EqualAsync(result.Unacknowledged.ToArray(),
            [new QueueWorkerClaimIdentity(held.Delivery.Id, held.Delivery.Token, held.Delivery.LeaseVersion,
                held.Delivery.LeaseUntil, held.Delivery.Attempt, held.Delivery.DeliveryGeneration)]);
        await Assert.That(result.Operations.Any(value => value.Command is not null)).IsFalse();
        var receive = await Assert.That(result.Operations.Where(value => value.Stage == QueueWorkerOperationStage.Returned
            && value.Receive?.RequestId == receiveId)).HasSingleItem();
        await Assert.That(receive.ReceiveFailure).IsNull();
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, held, QueueWorkerClusterProtocol.FirstReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Second,
            QueueWorkerClusterProtocol.SecondReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Third,
            QueueWorkerClusterProtocol.ThirdReadySequence, token);
        await QueueWorkerClusterRenewalContinuation.RunAsync(fixture, sdk, mcp, seed, held, token);
    }
}
