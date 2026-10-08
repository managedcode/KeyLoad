using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Encodes canonical database keys without changing their persisted ordering or scope.</summary>
public static class KeySpace
{
    private const string CatalogSpace = "catalog";
    private const string PrincipalSpace = "principal";
    private const string ApiKeySpace = "api-key";
    private const string ScopedOutcomeSpace = "outcome-v2";
    private const string GlobalOutcomeScope = "global";
    private const string UnknownOutcomeScope = "unknown";
    private const string ScopedOutcomeLocatorSpace = "outcome-locator-v2";
    private const string SystemSpace = "system";
    private const string AppliedName = "last-applied";
    private const string ClockName = "clock";

    internal static byte[] ResourcePrefix(string tenant, string database) => KeyCodec.Encode(CatalogSpace, tenant, database);

    internal static byte[] AppliedBytes { get; } = KeyCodec.Encode(SystemSpace, AppliedName);
    internal static byte[] ClockBytes { get; } = KeyCodec.Encode(SystemSpace, ClockName);

    /// <summary>Gets the read-only canonical apply-watermark key.</summary>
    public static ReadOnlyMemory<byte> Applied => AppliedBytes;

    /// <summary>Gets the read-only canonical committed-business-clock key.</summary>
    public static ReadOnlyMemory<byte> Clock => ClockBytes;

    /// <summary>Creates an owned key within one atomic partition.</summary>
    /// <param name="space">Named feature key space.</param>
    /// <param name="partition">Complete atomic partition identity.</param>
    /// <param name="suffix">Feature-specific ordered key components.</param>
    /// <returns>The canonical independent key buffer.</returns>
    public static byte[] Partition(string space, PartitionRef partition, params object?[] suffix)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(suffix);
        return KeyCodec.Encode([space, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, .. suffix]);
    }

    /// <summary>Creates an owned resource-catalog key within a tenant and database.</summary>
    /// <param name="tenant">Owning tenant identifier.</param>
    /// <param name="database">Owning database identifier.</param>
    /// <param name="resource">Resource identifier.</param>
    /// <returns>The canonical independent catalog key.</returns>
    public static byte[] Resource(string tenant, string database, string resource) => KeyCodec.Encode(CatalogSpace, tenant, database, resource);

    /// <summary>Creates an owned persisted-principal key.</summary>
    /// <param name="principal">Principal identifier.</param>
    /// <returns>The canonical independent principal key.</returns>
    public static byte[] Principal(string principal) => KeyCodec.Encode(PrincipalSpace, principal);

    /// <summary>Creates an owned API-credential key.</summary>
    /// <param name="id">Credential identifier.</param>
    /// <returns>The canonical independent credential key.</returns>
    public static byte[] ApiKey(string id) => KeyCodec.Encode(ApiKeySpace, id);

    internal static byte[] PartitionOutcome(PartitionRef partition, string principal, Guid id)
        => Partition(ScopedOutcomeSpace, partition, principal, id);

    internal static byte[] GlobalOutcome(string principal, Guid id)
        => KeyCodec.Encode(ScopedOutcomeSpace, GlobalOutcomeScope, principal, id);

    internal static byte[] UnknownOutcome(string principal, Guid id)
        => KeyCodec.Encode(ScopedOutcomeSpace, UnknownOutcomeScope, principal, id);

    internal static byte[] OutcomeLocatorV2(PartitionRef partition, string principal, Guid id)
        => Partition(ScopedOutcomeLocatorSpace, partition, principal, id);
}
