using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Graph.Interfaces;
using ManagedCode.Orleans.Graph.Models;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsLeafGrain : Grain, ICqrsLeafGrain
{
    public CqrsLeafGrain()
    {
    }

    public Task<CqrsLeafObservation> ObserveAsync(Guid requestId)
    {
        var history = RequestContext.Get(Constants.RequestContextKey) as CallHistory;
        var edge = history is null ? null : GrainTransitionManager.GetLatestObservedCall(history);
        var (outgoing, incoming) = FindLeafCalls(history);
        var observation = new CqrsLeafObservation(
            requestId,
            RequestContextHelper.CaptureCurrentCaller()?.ToString() ?? CqrsProtocol.Empty,
            typeof(ICqrsLeafGrain).FullName ?? CqrsProtocol.Empty,
            nameof(ICqrsLeafGrain.ObserveAsync),
            edge?.Source ?? CqrsProtocol.Empty,
            edge?.Target ?? CqrsProtocol.Empty,
            edge?.SourceMethod ?? CqrsProtocol.Empty,
            edge?.TargetMethod ?? CqrsProtocol.Empty,
            outgoing?.SourceId?.ToString() ?? CqrsProtocol.Empty,
            outgoing?.TargetId?.ToString() ?? CqrsProtocol.Empty,
            incoming?.SourceId?.ToString() ?? CqrsProtocol.Empty,
            incoming?.TargetId?.ToString() ?? CqrsProtocol.Empty,
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
        => string.Equals(call.Interface, typeof(ICqrsLeafGrain).FullName, StringComparison.Ordinal) &&
           string.Equals(call.Method, nameof(ICqrsLeafGrain.ObserveAsync), StringComparison.Ordinal);
}
