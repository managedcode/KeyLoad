using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Hashes only the exact retained native rows matured by this original local owner release.</summary>
internal static class ResourceRetirementOwnerReleaseAuthority
{
    internal static ResourceRetirementAuthoritySummary Capture(IKeyValueView view,
        ResourceRetirementOwnerReleaseV1 release, DatabaseLimits limits)
    {
        var lifetime = release.Finalization.Control.Lifetime;
        var owner = release.Receiver.Owner;
        var keys = new List<byte[]>
        {
            ResourceRetirementStorage.FenceKey(lifetime),
            ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace, lifetime, owner),
            ResourceRetirementAuthorityKeys.Release(lifetime, owner)
        };
        if (ResourceRetirementConservation.SameOwner(release.Finalization.Control.Catalog.DefaultShard, owner))
        {
            keys.Add(ResourceRetirementFinalizationKeys.History(lifetime));
        }
        var accumulator = new ResourceRetirementAuthorityAccumulator();
        var releaseKey = ResourceRetirementAuthorityKeys.Release(lifetime, owner);
        var releaseBytes = NativeSerialization.Serialize(release);
        foreach (var key in keys)
        {
            var found = view.ReadValue(key, value =>
            {
                if (!ResourceRetirementAuthorityClassifier.TryRead(key, value, limits.MaxBatchBytes, out var candidate)
                    || candidate is null || candidate.Lifetime != lifetime || candidate.PhysicalOwner is null
                    || !ResourceRetirementConservation.SameOwner(candidate.PhysicalOwner, owner)
                    || !candidate.IsReleasedBy(releaseKey, releaseBytes, limits.MaxBatchBytes))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
                }
                accumulator.Add(key, value);
            });
            if (!found)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        return accumulator.Complete();
    }
}
