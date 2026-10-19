using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsFatalStreamDrain
{
    internal static async Task DrainFatalPullAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedChunkCount).IsEqualTo(1);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task CancelAndDisposeAfterStartedAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, CancellationTokenSource cancellation, RequestCqrsFatalObservation observation)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellation.Token))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
            await cancellation.CancelAsync();
            break;
        }

        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task DrainCompletedAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedChunkCount).IsEqualTo(2);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedChunkKind(1)).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task DrainAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

}
