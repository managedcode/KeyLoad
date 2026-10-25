using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsStreamGrain : Grain, ICqrsStreamGrain
{
    public CqrsStreamGrain()
    {
    }

    public IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> AllowedAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateOutcomeStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> DeniedAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateOutcomeStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> BackpressureAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateBackpressureStream(requestId, cancellationToken);

    public IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> FailAfterProgressAsync(
        Guid requestId, CancellationToken cancellationToken)
        => CreateFailureStream(requestId, cancellationToken);

    private IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> CreateOutcomeStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<CqrsProgress, CqrsResult>(
            writer => RunOutcomeAsync(writer, requestId), cancellationToken);

    private async ValueTask<Result<CqrsResult>> RunOutcomeAsync(
        ICqrsStreamWriter<CqrsProgress, CqrsResult> writer,
        Guid requestId)
    {
        var observation = GrainFactory.GetGrain<ICqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new CqrsProgress(requestId, CqrsProtocol.Started));
            await observation.RecordAsync(CqrsProtocol.Started);
            await Task.Yield();
            await observation.RecordAsync(CqrsProtocol.LeafCallAttempted);
            var leaf = GrainFactory.GetGrain<ICqrsLeafGrain>(requestId);
            var result = await leaf.ObserveAsync(requestId);
            await observation.RecordObservationAsync(result);
            await writer.ProgressAsync(new CqrsProgress(requestId, CqrsProtocol.ProgressOne));
            await observation.RecordOutcomeAsync(succeeded: true);
            return Result<CqrsResult>.Succeed(new CqrsResult(requestId, result));
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }

    private IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> CreateBackpressureStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<CqrsProgress, CqrsResult>(
            writer => RunBackpressureAsync(writer, requestId), cancellationToken);

    private async ValueTask<CqrsResult> RunBackpressureAsync(
        ICqrsStreamWriter<CqrsProgress, CqrsResult> writer, Guid requestId)
    {
        var observation = GrainFactory.GetGrain<ICqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new CqrsProgress(requestId, CqrsProtocol.Started));
            await observation.RecordAsync(CqrsProtocol.Started);
            await writer.ProgressAsync(new CqrsProgress(requestId, CqrsProtocol.ProgressOne));
            await observation.RecordAsync(CqrsProtocol.ProgressOne);
            await observation.RecordAsync(CqrsProtocol.ProgressTwoAttempted);
            await writer.ProgressAsync(new CqrsProgress(requestId, CqrsProtocol.ProgressTwo));
            await observation.RecordAsync(CqrsProtocol.ProgressTwo);
            await observation.RecordOutcomeAsync(succeeded: true);
            return new CqrsResult(requestId, null);
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }

    private IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> CreateFailureStream(
        Guid requestId, CancellationToken cancellationToken)
        => CqrsStream.Create<CqrsProgress, CqrsResult>(
            writer => RunFailureAsync(writer, requestId), cancellationToken);

    private async ValueTask<Result<CqrsResult>> RunFailureAsync(
        ICqrsStreamWriter<CqrsProgress, CqrsResult> writer, Guid requestId)
    {
        var observation = GrainFactory.GetGrain<ICqrsObservationSinkGrain>(requestId);
        try
        {
            await writer.StartedAsync(new CqrsProgress(requestId, CqrsProtocol.Started));
            await observation.RecordAsync(CqrsProtocol.Started);
            await Task.Yield();
            await writer.ProgressAsync(new CqrsProgress(requestId, CqrsProtocol.ProgressOne));
            await observation.RecordAsync(CqrsProtocol.ProgressOne);
            await observation.RecordOutcomeAsync(succeeded: false);
            return Result<CqrsResult>.Fail(Problem.Create(
                CqrsProtocol.FailureTitle,
                CqrsProtocol.FailureDetail,
                CqrsProtocol.FailureStatus));
        }
        finally
        {
            await observation.MarkSettledAsync(writer.CancellationToken.IsCancellationRequested);
        }
    }
}
