using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Stores the existing owner's active admissions; every access requires that owner's gate.</summary>
internal sealed class NativeRequestWorkAdmissions
{
    private readonly Dictionary<(Guid RequestId, NativeRequestWorkKind Kind), NativeRequestWorkLease> active = [];
    private readonly GrainRoutingOptions settings;
    private long admissionGeneration;
    private long publicationGeneration;
    private int requestProducers;

    internal NativeRequestWorkAdmissions(GrainRoutingOptions settings) => this.settings = settings;
    internal int Count => active.Count;
    internal Task<NativeRequestWorkOriginalSettlement>[] Settlements
        => active.Values.Select(lease => lease.Settlement).ToArray();

    internal NativeRequestWorkLease Acquire(NativeRequestWorkOwner owner, Guid id, NativeRequestWorkKind kind)
    {
        var key = (id, kind);
        if (active.ContainsKey(key) || active.Count >= settings.MaximumTotalFrames
            || kind == NativeRequestWorkKind.RequestProducer && requestProducers >= settings.MaximumRequestProducers)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, NativeRequestWorkLimits.CapacityMessage);
        }
        var lease = new NativeRequestWorkLease(owner, id, kind, checked(++admissionGeneration));
        active.Add(key, lease);
        if (kind == NativeRequestWorkKind.RequestProducer)
        {
            requestProducers++;
        }
        return lease;
    }

    internal void Release(NativeRequestWorkLease lease)
    {
        RequireActive(lease);
        if (lease.HasRetirementHolds)
        {
            return;
        }
        Remove(lease);
    }

    private void Remove(NativeRequestWorkLease lease)
    {
        active.Remove((lease.RequestId, lease.Kind));
        if (lease.Kind == NativeRequestWorkKind.RequestProducer)
        {
            requestProducers--;
        }
    }

    internal void Bind(NativeRequestWorkLease lease, ImmutableArray<ResourceLifetimeV1> lifetimes)
    {
        RequireActive(lease);
        lease.RequirePublicationOpen();
        if (lifetimes.IsDefault || lifetimes.Any(lifetime => lifetime is null))
        {
            throw Errors.Fail(ErrorCode.Validation, NativeRequestWorkLimits.InvalidIdentityMessage);
        }
        // The authenticated same-cut caller validated this set; the original lease reserved its metadata.
        // An authorized resource-free command has an empty set, not a missing or inferred dependency.
        var parent = lease.ChildReservation?.Parent;
        if (parent is not null)
        {
            RequireActive(parent);
            parent.RequirePublicationOpen();
        }
        var completeSet = lease.Dependencies.Union(lifetimes);
        var parentSet = parent?.Dependencies.Union(lifetimes);
        lease.Dependencies = completeSet;
        if (parent is not null)
        {
            parent.Dependencies = parentSet!;
        }
    }

    internal void AdmitPublication(NativeRequestWorkLease lease)
    {
        RequireActive(lease);
        lease.RequirePublicationOpen();
        lease.LastPublicationGeneration = checked(++publicationGeneration);
    }

    internal ImmutableArray<NativeRequestWorkLease> CloseMatching(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        var matching = active.Values.Where(lease => lease.Dependencies.Contains(fence.Control.Lifetime)).ToImmutableArray();
        foreach (var lease in matching)
        {
            lease.MarkRetirement(fence, fingerprint);
        }
        return matching;
    }

    internal void ConsumeRetirement(NativeRequestWorkLease lease, ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        RequireActive(lease);
        lease.ConsumeRetirement(fence, fingerprint);
        if (!lease.HasRetirementHolds && lease.Settlement.IsCompleted)
        {
            Remove(lease);
        }
    }

    internal NativeRequestWorkChildReservation ReserveChild(NativeRequestWorkOwner owner,
        NativeRequestWorkLease parent, NativeRequestWorkChildIdentity identity)
    {
        RequireActive(parent);
        parent.RequirePublicationOpen();
        _ = parent.ResultRetention;
        var child = Acquire(owner, identity.RequestId, NativeRequestWorkKind.RequestProducer);
        child.AttachResultRetention(parent.ResultRetention);
        child.Dependencies = parent.Dependencies;
        parent.RetainCompletionObserver();
        var reservation = new NativeRequestWorkChildReservation(owner, parent, child, identity);
        child.ChildReservation = reservation;
        return reservation;
    }

    internal NativeRequestWorkLease RequireOriginalParentBinding(NativeRequestWorkLease child)
    {
        RequireActive(child);
        child.RequirePublicationOpen();
        var reservation = child.ChildReservation
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        reservation.RequireExecuting();
        var parent = reservation.Parent;
        RequireActive(parent);
        parent.RequirePublicationOpen();
        if (!ReferenceEquals(reservation.Child, child) || !ReferenceEquals(child.ResultRetention, parent.ResultRetention))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        return parent;
    }

    internal NativeCommandObservation FindObservation(NativeCommandObservationHandleV1 handle,
        PhysicalShardRecord actualOwner, ReplicaSiloDiscovery actualRuntime, KeyLoad.Core.DatabaseEngine database)
    {
        if (!active.TryGetValue((handle.ObservationId, NativeRequestWorkKind.ReadCapability), out var lease)
            || lease.CommandObservation is not { } observation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        RequireActive(lease);
        observation.RequireIdentity(handle, actualOwner, actualRuntime, database);
        return observation;
    }

    internal bool HasReservedChild(Guid requestId)
        => active.TryGetValue((requestId, NativeRequestWorkKind.RequestProducer), out var frame)
            && frame.ChildReservation is not null;

    internal NativeRequestWorkLease ConsumeChild(NativeRequestWorkChildIdentity identity)
    {
        if (!active.TryGetValue((identity.RequestId, NativeRequestWorkKind.RequestProducer), out var child)
            || child.ChildReservation is not { } reservation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        RequireActive(reservation.Parent);
        reservation.Parent.RequirePublicationOpen();
        child.RequirePublicationOpen();
        reservation.Consume(identity);
        return child;
    }

    internal void RecordChildCleanup(NativeRequestWorkLease parent, ImmutableArray<Exception> failures)
    {
        RequireActive(parent);
        if (!failures.IsEmpty)
        {
            parent.ClosePublication(retainCancellation: false);
            parent.ChildCleanupFailures = parent.ChildCleanupFailures.AddRange(failures);
        }
    }

    internal void RequireActive(NativeRequestWorkLease lease)
    {
        if (!active.TryGetValue((lease.RequestId, lease.Kind), out var actual) || !ReferenceEquals(actual, lease))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }
}
