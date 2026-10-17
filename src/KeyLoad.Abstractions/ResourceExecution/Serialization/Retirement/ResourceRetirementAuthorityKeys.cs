using KeyLoad.Storage;

namespace KeyLoad;

internal static class ResourceRetirementAuthorityKeys
{
    private const int IncarnationCharacters = 64;
    private const string Invalid = "The native retirement authority lifetime is invalid.";
    internal const string FenceSpace = "resource-retirement-fence-v1";
    internal const string FinalizationSpace = "resource-retirement-finalization-v1";
    internal const string CleanupSpace = "resource-retirement-owner-cleanup-v1";
    internal const string ReleaseSpace = "resource-retirement-owner-release-v1";

    internal static byte[] Scope(string space, ResourceLifetimeV1 lifetime)
    {
        RequireLifetime(lifetime);
        return KeyCodec.Encode(space, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value);
    }

    internal static byte[] Owner(string space, ResourceLifetimeV1 lifetime, PhysicalShardRecord owner)
    {
        RequireLifetime(lifetime);
        ResourceRetirementConservation.ValidateOwner(owner);
        return KeyCodec.Encode(space, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName,
            lifetime.Incarnation.Value, owner.PhysicalShardId, owner.Incarnation);
    }

    internal static byte[] ReleasePrefix() => KeyCodec.Encode(ReleaseSpace);

    internal static byte[] Release(ResourceLifetimeV1 lifetime, PhysicalShardRecord owner)
        => Owner(ReleaseSpace, lifetime, owner);

    private static void RequireLifetime(ResourceLifetimeV1 lifetime)
    {
        if (lifetime is null || string.IsNullOrWhiteSpace(lifetime.TenantId)
            || string.IsNullOrWhiteSpace(lifetime.DatabaseId) || string.IsNullOrWhiteSpace(lifetime.ResourceName)
            || string.IsNullOrWhiteSpace(lifetime.TransactionDomainId) || lifetime.Kind != ResourceKind.Collection
            || lifetime.Incarnation?.Value is not { Length: IncarnationCharacters } incarnation
            || !incarnation.All(char.IsAsciiHexDigitLower)
            || lifetime.CreationFingerprint is not { Length: IncarnationCharacters } fingerprint
            || !fingerprint.All(char.IsAsciiHexDigitLower))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}
