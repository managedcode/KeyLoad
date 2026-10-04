using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Graph.Interfaces;
using ManagedCode.Orleans.Graph.Models;

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

internal sealed class NativeCqrsLeafGrain : Grain, INativeCqrsLeafGrain
{
    public NativeCqrsLeafGrain()
    {
    }

    public Task<NativeCqrsLeafObservation> ObserveAsync(Guid requestId)
    {
        var history = RequestContext.Get(Constants.RequestContextKey) as CallHistory;
        var edge = history is null ? null : GrainTransitionManager.GetLatestObservedCall(history);
        var (outgoing, incoming) = FindLeafCalls(history);
        var observation = new NativeCqrsLeafObservation(
            requestId,
            RequestContextHelper.CaptureCurrentCaller()?.ToString() ?? NativeCqrsProtocol.Empty,
            typeof(INativeCqrsLeafGrain).FullName ?? NativeCqrsProtocol.Empty,
            nameof(INativeCqrsLeafGrain.ObserveAsync),
            edge?.Source ?? NativeCqrsProtocol.Empty,
            edge?.Target ?? NativeCqrsProtocol.Empty,
            edge?.SourceMethod ?? NativeCqrsProtocol.Empty,
            edge?.TargetMethod ?? NativeCqrsProtocol.Empty,
            outgoing?.SourceId?.ToString() ?? NativeCqrsProtocol.Empty,
            outgoing?.TargetId?.ToString() ?? NativeCqrsProtocol.Empty,
            incoming?.SourceId?.ToString() ?? NativeCqrsProtocol.Empty,
            incoming?.TargetId?.ToString() ?? NativeCqrsProtocol.Empty,
            this.GetPrimaryKey().ToString());
        return Task.FromResult(observation);
    }

    private static (OutCall? Outgoing, InCall? Incoming) FindLeafCalls(CallHistory? history)
    {
        OutCall? outgoing = null;
        InCall? incoming = null;
        if (history is null)
        {
            return (outgoing, incoming);
        }

        foreach (var call in history.History)
        {
            if (call is OutCall outCall && IsLeafCall(outCall))
            {
                outgoing = outCall;
            }
            else if (call is InCall inCall && IsLeafCall(inCall))
            {
                incoming = inCall;
            }
        }
        return (outgoing, incoming);
    }

    private static bool IsLeafCall(Call call)
        => string.Equals(call.Interface, typeof(INativeCqrsLeafGrain).FullName, StringComparison.Ordinal) &&
           string.Equals(call.Method, nameof(INativeCqrsLeafGrain.ObserveAsync), StringComparison.Ordinal);
}

internal sealed class NativeCqrsObservationGrain : Grain,
    INativeCqrsObservationSinkGrain,
    INativeCqrsObservationReaderGrain
{
    private readonly List<string> events = [];
    private NativeCqrsLeafObservation? leafObservation;
    private bool settled;
    private bool cancellationObserved;
    private bool handlerOutcomeReturned;
    private bool handlerOutcomeSucceeded;
    private int settlementCount;

    public NativeCqrsObservationGrain()
    {
    }

    public Task RecordAsync(string eventName)
    {
        events.Add(eventName);
        return Task.CompletedTask;
    }

    public Task RecordObservationAsync(NativeCqrsLeafObservation observation)
    {
        leafObservation = observation;
        return Task.CompletedTask;
    }

    public Task RecordOutcomeAsync(bool succeeded)
    {
        handlerOutcomeReturned = true;
        handlerOutcomeSucceeded = succeeded;
        return Task.CompletedTask;
    }

    public Task MarkSettledAsync(bool cancellationObserved)
    {
        settled = true;
        this.cancellationObserved = cancellationObserved;
        settlementCount++;
        events.Add(NativeCqrsProtocol.Settled);
        return Task.CompletedTask;
    }

    public Task<NativeCqrsObservationSnapshot> ReadAsync()
    {
        var snapshot = new NativeCqrsObservationSnapshot(
            this.GetPrimaryKey(), Array.AsReadOnly(events.ToArray()), settled, cancellationObserved, settlementCount,
            leafObservation, handlerOutcomeReturned, handlerOutcomeSucceeded);
        return Task.FromResult(snapshot);
    }
}
