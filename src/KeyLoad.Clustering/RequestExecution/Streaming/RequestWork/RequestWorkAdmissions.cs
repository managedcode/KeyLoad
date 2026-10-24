using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Stores the existing owner's active admissions; every access requires that owner's gate.</summary>
internal sealed class RequestWorkAdmissions
{
    private readonly Dictionary<(Guid RequestId, RequestWorkKind Kind), RequestWorkLease> active = [];
    private long admissionGeneration;
    private long publicationGeneration;

    internal int Count => active.Count;
    internal Task<RequestWorkOriginalSettlement>[] Settlements
        => active.Values.Select(lease => lease.Settlement).ToArray();

    internal RequestWorkLease Acquire(RequestWorkOwner owner, Guid id, RequestWorkKind kind)
    {
        var key = (id, kind);
        if (active.ContainsKey(key))
        {
            throw Errors.Fail(ErrorCode.Conflict, RequestWorkLimits.DuplicateMessage);
        }
        var lease = new RequestWorkLease(owner, id, kind, checked(++admissionGeneration));
        active.Add(key, lease);
        return lease;
    }

    internal void Release(RequestWorkLease lease)
    {
        RequireActive(lease);
        active.Remove((lease.RequestId, lease.Kind));
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
