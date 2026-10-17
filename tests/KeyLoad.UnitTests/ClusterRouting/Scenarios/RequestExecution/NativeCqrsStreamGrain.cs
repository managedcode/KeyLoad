using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsStreamGrain : Grain, INativeCqrsStreamGrain
{
    public NativeCqrsStreamGrain()
    {
    }

    public IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> AllowedAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateOutcomeStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> DeniedAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateOutcomeStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> BackpressureAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateBackpressureStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> FailAfterProgressAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateFailureStream(requestId, cancellationToken);

    private IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> CreateOutcomeStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<NativeCqrsProgress, NativeCqrsResult>(
            writer => RunOutcomeAsync(writer, requestId), cancellationToken);

    private async ValueTask<Result<NativeCqrsResult>> RunOutcomeAsync(
        ICqrsStreamWriter<NativeCqrsProgress, NativeCqrsResult> writer,
        Guid requestId)
    {
        var observation = GrainFactory.GetGrain<INativeCqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.Started));
            await observation.RecordAsync(NativeCqrsProtocol.Started);
            await Task.Yield();
            await observation.RecordAsync(NativeCqrsProtocol.LeafCallAttempted);
            var leaf = GrainFactory.GetGrain<INativeCqrsLeafGrain>(requestId);
            var result = await leaf.ObserveAsync(requestId);
            await observation.RecordObservationAsync(result);
            await writer.ProgressAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.ProgressOne));
            await observation.RecordOutcomeAsync(succeeded: true);
            return Result<NativeCqrsResult>.Succeed(new NativeCqrsResult(requestId, result));
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }

    private IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> CreateBackpressureStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<NativeCqrsProgress, NativeCqrsResult>(
            writer => RunBackpressureAsync(writer, requestId), cancellationToken);

    private async ValueTask<NativeCqrsResult> RunBackpressureAsync(
        ICqrsStreamWriter<NativeCqrsProgress, NativeCqrsResult> writer, Guid requestId)
    {
        var observation = GrainFactory.GetGrain<INativeCqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.Started));
            await observation.RecordAsync(NativeCqrsProtocol.Started);
            await writer.ProgressAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.ProgressOne));
            await observation.RecordAsync(NativeCqrsProtocol.ProgressOne);
            await observation.RecordAsync(NativeCqrsProtocol.ProgressTwoAttempted);
            await writer.ProgressAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.ProgressTwo));
            await observation.RecordAsync(NativeCqrsProtocol.ProgressTwo);
            await observation.RecordOutcomeAsync(succeeded: true);
            return new NativeCqrsResult(requestId, null);
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }

    private IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> CreateFailureStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<NativeCqrsProgress, NativeCqrsResult>(
            writer => RunFailureAsync(writer, requestId), cancellationToken);

    private async ValueTask<Result<NativeCqrsResult>> RunFailureAsync(
        ICqrsStreamWriter<NativeCqrsProgress, NativeCqrsResult> writer, Guid requestId)
    {
        var observation = GrainFactory.GetGrain<INativeCqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.Started));
            await observation.RecordAsync(NativeCqrsProtocol.Started);
            await Task.Yield();
            await writer.ProgressAsync(new NativeCqrsProgress(requestId, NativeCqrsProtocol.ProgressOne));
            await observation.RecordAsync(NativeCqrsProtocol.ProgressOne);
            await observation.RecordOutcomeAsync(succeeded: false);
            return Result<NativeCqrsResult>.Fail(Problem.Create(
                NativeCqrsProtocol.FailureTitle,
                NativeCqrsProtocol.FailureDetail,
                NativeCqrsProtocol.FailureStatus));
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }
}
