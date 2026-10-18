using System.Collections.Concurrent;
using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Holds actual native search after canonical range reads and joins its original producer.</summary>
internal sealed class QueryHostedReadObserver : IGrainRequestPhaseObserver
{
    private const string ChangedOwner = "The native query ran outside its expected original silo and producer.";
    private readonly ConcurrentDictionary<Guid, QueryHostedReadObservation> reads = new();

    internal QueryHostedReadObservation Hold(Guid requestId, RequestCqrsHostedRuntimeFixture node)
    {
        var observation = new QueryHostedReadObservation(node.Database.Store);
        if (!reads.TryAdd(requestId, observation))
        {
            throw new InvalidOperationException(ChangedOwner);
        }
        return observation;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token)
    {
        if (!reads.TryGetValue(identity.RequestId, out var read))
        {
            return;
        }
        var database = context.ActivationServices.GetRequiredService<DatabaseEngine>();
        if (!ReferenceEquals(database.Store, read.Store))
        {
            throw new InvalidOperationException(ChangedOwner);
        }
        if (phase == GrainRequestPhase.RequestStarted)
        {
            read.Identity = identity;
            read.GrainId = context.GrainId;
            read.ActivationId = context.ActivationId;
            read.NodeId = database.Store.Identity.NodeId;
        }
        if (phase == GrainRequestPhase.DistributedSearchStatisticsCaptured)
        {
            RequireOriginal(read, identity, context);
            read.ObservedBytes = read.Store.GetReadDiagnostics().RangeExaminedBytes;
            if (read.ObservedBytes <= read.BeforeBytes)
            {
                throw new InvalidOperationException("The native query reached statistics without reading canonical records.");
            }
            read.Entered.TrySetResult();
            await read.Release.Task.WaitAsync(token).ConfigureAwait(true);
        }
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (reads.TryGetValue(identity.RequestId, out var read))
        {
            RequireOriginal(read, identity, context);
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

    private static void RequireOriginal(QueryHostedReadObservation read, GrainRequestProbeIdentity identity,
        IGrainContext context)
    {
        if (read.Identity != identity || read.GrainId != context.GrainId || read.ActivationId != context.ActivationId)
        {
            throw new InvalidOperationException(ChangedOwner);
        }
    }
}
