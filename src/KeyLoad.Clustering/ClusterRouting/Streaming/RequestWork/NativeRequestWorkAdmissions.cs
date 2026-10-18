using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Stores the existing owner's active admissions; every access requires that owner's gate.</summary>
internal sealed class NativeRequestWorkAdmissions(IOptions<GrainRoutingOptions> options)
{
    private readonly Dictionary<(Guid RequestId, NativeRequestWorkKind Kind), NativeRequestWorkLease> active = [];
    private readonly GrainRoutingOptions settings = options.Value;
    private long admissionGeneration;
    private long publicationGeneration;
    private int requestProducers;

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
        lease.Dependencies = lease.Dependencies.Union(lifetimes);
    }

    internal void AdmitPublication(NativeRequestWorkLease lease)
    {
        RequireActive(lease);
        lease.RequirePublicationOpen();
        lease.LastPublicationGeneration = checked(++publicationGeneration);
    }

    internal void RequireActive(NativeRequestWorkLease lease)
    {
        if (!active.TryGetValue((lease.RequestId, lease.Kind), out var actual) || !ReferenceEquals(actual, lease))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }
}
