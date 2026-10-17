using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryCatalog
{
    internal static ResourceOwnerInventoryOriginV1? ReadOrigin(IKeyValueView view, DatabaseLimits limits)
    {
        var bytes = view.ReadOwnedValue(ResourceOwnerInventoryKeys.Origin());
        if (bytes is null)
        {
            return null;
        }
        var origin = ResourceOwnerInventoryValidation.Decode<ResourceOwnerInventoryOriginV1>(bytes, limits);
        ResourceOwnerInventoryValidation.Origin(origin, requireProven: false);
        return origin;
    }

    internal static long RequireRevision(IKeyValueView view, Guid storeIncarnation, DatabaseLimits limits)
    {
        var origin = ReadOrigin(view, limits);
        if (origin is null || !origin.Proven || origin.StoreIncarnation != storeIncarnation)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown);
        }
        RequireSettledImports(view);
        return origin.Revision;
    }

    internal static void RequireSettledImports(IKeyValueView view)
        => view.VisitRange(KeyCodec.Encode(ResourceOwnerInventoryMovementProtocol.ImportSpace),
            ResourceOwnerInventoryMovementProtocol.ActiveImportProbeCount, (key, value) =>
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown));

    internal static byte[] Prefix(ResourceLifetimeV1 lifetime)
    {
        ResourceOwnerInventoryValidation.Lifetime(lifetime);
        return ResourceOwnerInventoryKeys.Prefix(lifetime);
    }

    internal static byte[] PartitionKey(ResourceOwnerInventoryEnrollmentV1 entry)
    {
        ResourceOwnerInventoryValidation.Enrollment(entry, requireProven: false);
        return ResourceOwnerInventoryKeys.Partition(entry);
    }

    internal static ResourceOwnerInventoryEnrollmentV1 ReadEntry(ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        var entry = ResourceOwnerInventoryValidation.Decode<ResourceOwnerInventoryEnrollmentV1>(value, limits);
        ResourceOwnerInventoryValidation.Enrollment(entry, requireProven: true);
        if (entry.Lifetime != lifetime || key.Length > limits.MaxBatchBytes
            || !key.SequenceEqual(ResourceOwnerInventoryKeys.Entry(lifetime, entry.Partition)))
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        return entry;
    }

    internal static ResourceOwnerInventoryEnrollmentV1 ReadPartitionEntry(ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, PartitionRef expectedPartition, DatabaseLimits limits)
    {
        var entry = ResourceOwnerInventoryValidation.Decode<ResourceOwnerInventoryEnrollmentV1>(value, limits);
        ResourceOwnerInventoryValidation.Enrollment(entry, requireProven: false);
        if (entry.Partition != expectedPartition || key.Length > limits.MaxBatchBytes
            || !key.SequenceEqual(ResourceOwnerInventoryKeys.Partition(entry)))
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        return entry;
    }

    internal static void InitializeFresh(IAtomicTransaction transaction, StoreIdentity identity,
        PhysicalShardRecord owner, DatabaseLimits limits)
        => ResourceOwnerInventoryMutations.InitializeFresh(transaction, identity, owner, limits);
    internal static void Import(IAtomicTransaction transaction, ResourceOwnerInventoryEnrollmentV1 entry,
        DatabaseLimits limits) => ResourceOwnerInventoryMutations.Import(transaction, entry, limits);
    internal static void Invalidate(IAtomicTransaction transaction, DatabaseLimits limits)
        => ResourceOwnerInventoryMutations.Invalidate(transaction, limits);
    internal static void RestoreVerified(IAtomicTransaction transaction, StoreIdentity identity,
        ResourceOwnerInventoryOriginV1 sourceOrigin, DatabaseLimits limits)
        => ResourceOwnerInventoryMutations.RestoreVerified(transaction, identity, sourceOrigin, limits);
}
