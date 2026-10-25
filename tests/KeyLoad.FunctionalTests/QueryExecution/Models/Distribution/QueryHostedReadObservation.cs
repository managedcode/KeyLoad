using KeyLoad.Clustering;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryHostedReadObservation(Guid connectionId, SiloAddress originSilo,
    GrainId expectedProducer, string principalId, int partitions)
{
    internal const string ChangedOwner = "The execution query changed its original request, producer or physical owner.";
    private readonly Lock gate = new();
    private readonly Dictionary<ZoneTreeStore, QueryHostedPhysicalRead> physicalReads = [];
    private readonly Dictionary<Guid, (GrainRequestProbeIdentity Identity, QueryHostedStorageOwner Owner)> children = [];
    private readonly int maximumChildren = checked(partitions * Enum.GetValues<DistributedSearchPhase>().Length);
    internal Guid ConnectionId { get; } = connectionId;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal GrainRequestProbeIdentity Identity { get; private set; }
    internal GrainId GrainId { get; private set; }
    internal ActivationId ActivationId { get; private set; }
    internal SiloAddress ProducerSilo { get; private set; } = null!;
    internal QueryHostedStorageOwner Coordinator { get; private set; } = null!;
    internal QueryHostedStorageOwner[] LeafOwners
    {
        get
        {
            lock (gate)
            {
                return [.. children.Values.Select(child => child.Owner)];
            }
        }
    }
    internal QueryHostedPhysicalRead[] PhysicalReads
    {
        get
        {
            lock (gate)
            {
                return [.. physicalReads.Values];
            }
        }
    }

    internal void Start(GrainRequestProbeIdentity identity, IGrainContext context, GrainRequestContextState state)
    {
        lock (gate)
        {
            RequireOriginalContext(identity, state);
            if (Identity.RequestId != Guid.Empty || identity.ReadKind != GrainReadKind.DistributedSearch
                || identity.CommandKind is not null || identity.PrincipalId != principalId
                || context.GrainId != expectedProducer || context.GrainInstance is not ConnectionGrain)
            {
                throw new InvalidOperationException(ChangedOwner);
            }
            Identity = identity;
            GrainId = context.GrainId;
            ActivationId = context.ActivationId;
            ProducerSilo = QueryHostedReadObserver.LocalSilo(context);
        }
    }

    internal void BindCoordinator(GrainRequestProbeIdentity identity, IGrainContext context, GrainRequestContextState state)
    {
        lock (gate)
        {
            RequireOriginalContext(identity, state);
            if (Identity != identity || Coordinator is not null)
            {
                throw new InvalidOperationException(ChangedOwner);
            }
            Coordinator = QueryHostedStorageOwner.Capture(context);
        }
    }

    internal void BindChild(GrainRequestProbeIdentity identity, IGrainContext context, GrainRequestContextState state)
    {
        lock (gate)
        {
            if (Coordinator is null || identity.ReadKind != GrainReadKind.DistributedSearchLeaf
                || identity.PrincipalId != Identity.PrincipalId || identity.CommandKind is not null
                || identity.CommandId != Guid.Empty || identity.RequestId == Identity.RequestId
                || identity.OriginalExpiresAt == default || identity.OriginalExpiresAt > Identity.OriginalExpiresAt
                || state.RequestId != identity.RequestId || state.CommandId != Guid.Empty
                || state.ConnectionId != ConnectionId || !Equals(state.OriginSilo, Coordinator.Silo)
                || children.Count >= maximumChildren || children.ContainsKey(identity.RequestId))
            {
                throw new InvalidOperationException(ChangedOwner);
            }
            var owner = QueryHostedStorageOwner.Capture(context);
            children.Add(identity.RequestId, (identity, owner));
            if (physicalReads.TryGetValue(owner.Store, out var retained))
            {
                if (!Equals(retained.Owner.Silo, owner.Silo) || retained.Owner.NodeId != owner.NodeId
                    || !ReferenceEquals(retained.Owner.Clock, owner.Clock))
                {
                    throw new InvalidOperationException(ChangedOwner);
                }
                return;
            }
            physicalReads.Add(owner.Store, new(owner));
        }
    }

    internal void CaptureStatistics(GrainRequestProbeIdentity identity, IGrainContext context,
        GrainRequestContextState state)
    {
        lock (gate)
        {
            RequireOriginalContext(identity, state);
            if (Identity != identity || Coordinator != QueryHostedStorageOwner.Capture(context)
                || children.Count != partitions || physicalReads.Count == 0)
            {
                throw new InvalidOperationException(ChangedOwner);
            }
            foreach (var read in physicalReads.Values)
            {
                read.CaptureProgress();
            }
        }
    }

    internal void RequireProducer(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (Identity != identity || GrainId != context.GrainId || ActivationId != context.ActivationId
            || !Equals(ProducerSilo, QueryHostedReadObserver.LocalSilo(context)))
        {
            throw new InvalidOperationException(ChangedOwner);
        }
    }

    private void RequireOriginalContext(GrainRequestProbeIdentity identity, GrainRequestContextState state)
    {
        if (identity.RequestId == Guid.Empty || identity.CommandId != Guid.Empty
            || state.RequestId != identity.RequestId || state.CommandId != Guid.Empty
            || state.ConnectionId != ConnectionId || !Equals(state.OriginSilo, originSilo))
        {
            throw new InvalidOperationException(ChangedOwner);
        }
    }
}
