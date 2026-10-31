using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterContinuation
{
    internal static async Task VerifyAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, QueueWorkerClusterHandlerLifecycle handlers, QueueWorkerResult result,
        Guid firstReceive, bool cancel, CancellationToken token)
    {
        await Assert.That(result.Reason).IsEqualTo(cancel ? QueueWorkerStopReason.Cancelled : QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerClusterProtocol.InvocationDeliveries);
        await Assert.That(handlers.Exited).IsEqualTo(handlers.Entered);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerClusterProtocol.ConcurrentHandlerLimit);
        await Assert.That(result.Operations.Any(row => row.Stage == QueueWorkerOperationStage.Prepared
            && row.Receive?.RequestId == firstReceive)).IsTrue();
        var acknowledgements = result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack && row.Completion?.IsSuccess == true).ToArray();
        await Assert.That(result.Acknowledged).IsEqualTo(acknowledgements.Length);
        if (cancel)
        {
            await Assert.That(acknowledgements.Any(row => row.Command!.Token
                == handlers.Contexts[QueueWorkerClusterProtocol.First].Delivery.Token)).IsFalse();
        }
        else
        {
            await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.InvocationDeliveries);
            await Assert.That(result.Unacknowledged).IsEmpty();
            await Assert.That(result.Failures).IsEmpty();
        }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var producer = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerClusterReplay.AcksAsync(sdk, producer, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerClusterEmptyBudget.RunAsync(sdk, mcp, seed, token);
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerClusterReplay.AcksAsync(sdk, producer, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerClusterHealthy.RunAsync(sdk, mcp, seed, token);
    }

    internal static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerClusterHandlerLifecycle handlers, QueueWorkerOperationObservation[] acknowledgements, CancellationToken token)
    {
        foreach (var id in QueueWorkerClusterProtocol.Messages)
        {
            var context = handlers.Contexts[id];
            var sequence = Array.IndexOf(QueueWorkerClusterProtocol.Messages, id) + QueueWorkerClusterProtocol.ReadySequenceOffset;
            if (acknowledgements.Any(row => row.Command!.CommandId == context.AcknowledgementId))
            {
                await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            }
            else
            {
                await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, context, sequence, token);
            }
        }
    }
}
