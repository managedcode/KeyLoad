using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Names the one native issuer's conserved rows; no alternate ledger or storage owner.</summary>
internal static class ResourceRetirementConservationKeys
{
    private const string FloorSpace = "resource-retirement-conservation-floor-v1";
    private const string StepSpace = "resource-retirement-conservation-step-v1";
    private const string ReleaseSpace = "resource-retirement-conservation-release-v1";

    internal static byte[] Floor() => KeyCodec.Encode(FloorSpace);

    internal static byte[] StepPrefix() => KeyCodec.Encode(StepSpace);

    internal static byte[] StepOwnerPrefix(PhysicalShardRecord owner)
    {
        ResourceRetirementConservation.ValidateOwner(owner);
        return KeyCodec.Encode(StepSpace, owner.PhysicalShardId, owner.Incarnation);
    }

    internal static byte[] ReleasePrefix() => KeyCodec.Encode(ReleaseSpace);

    internal static byte[] Step(PhysicalShardRecord owner, long revision)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        return KeyCodec.Encode(StepSpace, owner.PhysicalShardId, owner.Incarnation, revision);
    }

    internal static byte[] Release(ResourceLifetimeV1 lifetime, PhysicalShardRecord owner)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(lifetime.Incarnation);
        ArgumentNullException.ThrowIfNull(owner);
        return KeyCodec.Encode(ReleaseSpace, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName,
            lifetime.Incarnation.Value, owner.PhysicalShardId, owner.Incarnation);
    }
}
