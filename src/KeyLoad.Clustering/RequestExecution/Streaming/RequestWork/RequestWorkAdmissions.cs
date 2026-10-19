using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Stores the existing owner's active admissions; every access requires that owner's gate.</summary>
internal sealed class RequestWorkAdmissions(IOptions<GrainRoutingOptions> options)
{
    private readonly Dictionary<(Guid RequestId, RequestWorkKind Kind), RequestWorkLease> active = [];
    private readonly GrainRoutingOptions settings = options.Value;
    private long admissionGeneration;
    private long publicationGeneration;
    private int requestProducers;

    internal int Count => active.Count;
    internal Task<RequestWorkOriginalSettlement>[] Settlements
        => active.Values.Select(lease => lease.Settlement).ToArray();

    internal RequestWorkLease Acquire(RequestWorkOwner owner, Guid id, RequestWorkKind kind)
    {
        var key = (id, kind);
        if (active.ContainsKey(key) || active.Count >= settings.MaximumTotalFrames
            || kind == RequestWorkKind.RequestProducer && requestProducers >= settings.MaximumRequestProducers)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RequestWorkLimits.CapacityMessage);
        }
        var lease = new RequestWorkLease(owner, id, kind, checked(++admissionGeneration));
        active.Add(key, lease);
        if (kind == RequestWorkKind.RequestProducer)
        {
            requestProducers++;
        }
        return lease;
    }

    internal void Release(RequestWorkLease lease)
    {
        RequireActive(lease);
        active.Remove((lease.RequestId, lease.Kind));
        if (lease.Kind == RequestWorkKind.RequestProducer)
        {
            requestProducers--;
        }
    }

    internal void Bind(RequestWorkLease lease, ImmutableArray<ResourceLifetime> lifetimes)
    {
        RequireActive(lease);
        lease.RequirePublicationOpen();
        if (lifetimes.IsDefault || lifetimes.Any(lifetime => lifetime is null))
        {
            throw Errors.Fail(ErrorCode.Validation, RequestWorkLimits.InvalidIdentityMessage);
        }
        // The authenticated same-cut caller validated this set; the original lease reserved its metadata.
        // An authorized resource-free command has an empty set, not a missing or inferred dependency.
        lease.Dependencies = lease.Dependencies.Union(lifetimes);
    }

    internal void AdmitPublication(RequestWorkLease lease)
    {
        RequireActive(lease);
        lease.RequirePublicationOpen();
        lease.LastPublicationGeneration = checked(++publicationGeneration);
    }

    internal void RequireActive(RequestWorkLease lease)
    {
        if (!active.TryGetValue((lease.RequestId, lease.Kind), out var actual) || !ReferenceEquals(actual, lease))
        {
            throw Errors.Fail(ErrorCode.Corruption, RequestWorkLimits.LeaseIdentityMessage);
        }
    }
}
