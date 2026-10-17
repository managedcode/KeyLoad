using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Graph.Interfaces;
using ManagedCode.Orleans.Graph.Models;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

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
