using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Binds conserved authority to the existing committed physical catalog, including before a first local floor.</summary>
internal static class ResourceRetirementConservationCatalog
{
    private const string CatalogSpace = "physical-shard-catalog";
    private const string CatalogVersion = "v1";
    private const int CurrentVersion = 1;
    private const long InitialRevision = 1;
    private const string Invalid = "The retirement conservation owner does not match the committed physical shard catalog.";

    internal static byte[] Key() => KeyCodec.Encode(CatalogSpace, CatalogVersion);

    internal static void RequireOwner(IKeyValueView view, PhysicalShardRecord owner, int maximumRecordBytes,
        StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        ResourceRetirementConservation.ValidateOwner(owner);
        var found = false;
        view.ReadValue(Key(), value =>
        {
            RequireOwner(value, owner, maximumRecordBytes);
            found = true;
        }, observer);
        if (!found)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
    }

    internal static void RequireOwner(ReadOnlySpan<byte> catalogBytes, PhysicalShardRecord owner, int maximumRecordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        ResourceRetirementConservation.ValidateOwner(owner);
        if (catalogBytes.Length > maximumRecordBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid);
        }
        var catalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(catalogBytes);
        if (catalog is null || catalog.Version != CurrentVersion || catalog.Revision < InitialRevision
            || !ResourceRetirementConservation.SameOwner(catalog.DefaultShard, owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
    }
}
