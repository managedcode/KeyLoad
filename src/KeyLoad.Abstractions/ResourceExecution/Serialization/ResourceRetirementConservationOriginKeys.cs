using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Names immutable source provenance without replacing either owner's conservation history.</summary>
internal static class ResourceRetirementConservationOriginKeys
{
    private const string OriginSpace = "resource-retirement-conservation-origin-v1";

    internal static byte[] Prefix() => KeyCodec.Encode(OriginSpace);

    internal static byte[] Row(PhysicalShardRecord sourceOwner, PhysicalShardRecord targetOwner)
    {
        ArgumentNullException.ThrowIfNull(sourceOwner);
        ArgumentNullException.ThrowIfNull(targetOwner);
        return KeyCodec.Encode(OriginSpace, sourceOwner.PhysicalShardId, sourceOwner.Incarnation,
            targetOwner.PhysicalShardId, targetOwner.Incarnation);
    }
}
