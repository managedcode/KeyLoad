using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Continuation
{
    internal static async Task VerifyAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, QueueWorkerRf3HandlerLifecycle handlers, QueueWorkerResult result,
        Guid firstReceive, bool cancel, CancellationToken token)
    {
        await Assert.That(result.Reason).IsEqualTo(cancel ? QueueWorkerStopReason.Cancelled : QueueWorkerStopReason.DeliveryBudget);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerRf3Protocol.InvocationDeliveries);
        await Assert.That(handlers.Exited).IsEqualTo(handlers.Entered);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerRf3Protocol.ConcurrentHandlerLimit);
        var expectedBytes = (Encoding.UTF8.GetByteCount(QueueWorkerRf3Protocol.Payload)
            + Encoding.UTF8.GetByteCount(QueueWorkerRf3Protocol.Headers)) * QueueWorkerRf3Protocol.ConcurrentHandlerLimit;
        await Assert.That(result.PeakInFlightBytes).IsEqualTo((long)expectedBytes);
        await Assert.That(result.Operations.Any(row => row.Stage == QueueWorkerOperationStage.Prepared
            && row.Receive?.RequestId == firstReceive)).IsTrue();
        var acknowledgements = result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Ack && row.Completion?.IsSuccess == true).ToArray();
        await Assert.That(result.Acknowledged).IsEqualTo(acknowledgements.Length);
        if (cancel)
        {
            await Assert.That(acknowledgements.Any(row => row.Command!.Token
                == handlers.Contexts[QueueWorkerRf3Protocol.First].Delivery.Token)).IsFalse();
        }
        else
        {
            await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.InvocationDeliveries);
            await Assert.That(result.Unacknowledged).IsEmpty();
            await Assert.That(result.Failures).IsEmpty();
        }
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerRf3Replay.AcksAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerRf3EmptyBudget.RunAsync(sdk, mcp, seed, token);
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await ImagesAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerRf3Replay.AcksAsync(sdk, mcp, seed, handlers, acknowledgements, token);
        await QueueWorkerRf3Healthy.RunAsync(sdk, mcp, seed, token);
    }

    internal static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerRf3HandlerLifecycle handlers, QueueWorkerOperationObservation[] acknowledgements, CancellationToken token)
    {
        foreach (var id in QueueWorkerRf3Protocol.Messages)
        {
            var context = handlers.Contexts[id];
            var sequence = Array.IndexOf(QueueWorkerRf3Protocol.Messages, id) + QueueWorkerRf3Protocol.ReadySequenceOffset;
            if (acknowledgements.Any(row => row.Command!.CommandId == context.AcknowledgementId))
            {
                await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, id, sequence, token);
            }
            else
            {
                await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, context, sequence, token);
            }
        }
    }
}
