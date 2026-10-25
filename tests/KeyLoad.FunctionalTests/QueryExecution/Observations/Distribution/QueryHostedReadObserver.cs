using System.Collections.Concurrent;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>Holds actual execution search after canonical range reads and joins its original producer.</summary>
internal sealed class QueryHostedReadObserver : IGrainRequestPhaseObserver
{
    private readonly ConcurrentDictionary<Guid, QueryHostedReadObservation> reads = new();

    internal QueryHostedReadObservation Hold(Guid requestId, RequestCqrsHostedRuntimeFixture node,
        QueryHostedSearchDataset data)
    {
        var observation = new QueryHostedReadObservation(node.ConnectionId,
            node.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress,
            node.Grains.GetGrain<IConnectionGrain>(node.ConnectionId).GetGrainId(),
            data.Principal.Id, data.Request.Partitions.Length);
        if (!reads.TryAdd(requestId, observation))
        {
            throw new InvalidOperationException(QueryHostedReadObservation.ChangedOwner);
        }
        return observation;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token)
    {
        if (!reads.TryGetValue(identity.RequestId, out var read))
        {
            ObserveChild(identity, phase, context);
            return;
        }
        var state = RequireContext(identity);
        if (phase == GrainRequestPhase.RequestStarted)
        {
            read.Start(identity, context, state);
        }
        if (phase == GrainRequestPhase.AuthorizationReload)
        {
            read.BindCoordinator(identity, context, state);
        }
        if (phase == GrainRequestPhase.DistributedSearchStatisticsCaptured)
        {
            read.CaptureStatistics(identity, context, state);
            read.Entered.TrySetResult();
            await read.Release.Task.WaitAsync(token).ConfigureAwait(true);
        }
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (reads.TryGetValue(identity.RequestId, out var read))
        {
            read.RequireProducer(identity, context);
            read.Disposed.TrySetResult();
        }
    }

    internal void ReleaseAll()
    {
        foreach (var read in reads.Values)
        {
            read.Release.TrySetResult();
        }
    }

    internal static SiloAddress LocalSilo(IGrainContext context)
        => context.ActivationServices.GetRequiredService<ILocalSiloDetails>().SiloAddress;

    private void ObserveChild(GrainRequestProbeIdentity identity, GrainRequestPhase phase, IGrainContext context)
    {
        if (phase != GrainRequestPhase.AuthorizationReload || identity.ReadKind != GrainReadKind.DistributedSearchLeaf)
        {
            return;
        }
        var state = RequireContext(identity);
        var selected = reads.Values.SingleOrDefault(read => !read.Disposed.Task.IsCompleted
            && read.Identity.RequestId != Guid.Empty && read.ConnectionId == state.ConnectionId);
        selected?.BindChild(identity, context, state);
    }

    private static GrainRequestContextState RequireContext(GrainRequestProbeIdentity identity)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
            || state.RequestId != identity.RequestId || state.CommandId != identity.CommandId)
        {
            throw new InvalidOperationException(QueryHostedReadObservation.ChangedOwner);
        }
        return state;
    }
}
