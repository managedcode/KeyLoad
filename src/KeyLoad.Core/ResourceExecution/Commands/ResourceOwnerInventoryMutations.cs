using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMutations
{
    internal static void InitializeFresh(IAtomicTransaction transaction, StoreIdentity identity,
        PhysicalShardRecord owner, DatabaseLimits limits)
    {
        if (identity.Incarnation == Guid.Empty || identity.Incarnation != owner.Incarnation)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        if (current is not null)
        {
            if (!current.Proven || current.StoreIncarnation != identity.Incarnation)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown);
            }
            return;
        }
        PutOrigin(transaction, new(ResourceOwnerInventoryProtocol.Version, identity.Incarnation,
            true, ResourceOwnerInventoryProtocol.Next), limits);
    }

    internal static void Invalidate(IAtomicTransaction transaction, DatabaseLimits limits)
    {
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        PutOrigin(transaction, new(ResourceOwnerInventoryProtocol.Version,
            current?.StoreIncarnation ?? Guid.Empty, false, Next(current)), limits);
    }

    internal static void RestoreVerified(IAtomicTransaction transaction, StoreIdentity identity,
        ResourceOwnerInventoryOriginV1 sourceOrigin, DatabaseLimits limits)
    {
        ResourceOwnerInventoryValidation.Origin(sourceOrigin, requireProven: true);
        if (identity.Incarnation == Guid.Empty)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        PutOrigin(transaction, new(ResourceOwnerInventoryProtocol.Version, identity.Incarnation,
            true, Next(current)), limits);
    }

    internal static void Import(IAtomicTransaction transaction, ResourceOwnerInventoryEnrollmentV1 entry,
        DatabaseLimits limits)
    {
        ResourceOwnerInventoryValidation.Enrollment(entry, requireProven: true);
        var bytes = ResourceOwnerInventoryValidation.Encode(entry, limits);
        var memberKey = ResourceOwnerInventoryKeys.Partition(entry);
        var member = transaction.ReadOwnedValue(memberKey);
        if (member is not null)
        {
            var previous = ResourceOwnerInventoryCatalog.ReadPartitionEntry(memberKey, member, entry.Partition, limits);
            if (previous.Lifetime != entry.Lifetime)
            {
                throw ResourceOwnerInventoryValidation.Invalid();
            }
        }
        transaction.Put(memberKey, bytes);
        var reverseKey = ResourceOwnerInventoryKeys.Entry(entry.Lifetime, entry.Partition);
        var reverse = transaction.ReadOwnedValue(reverseKey);
        if (reverse is not null)
        {
            ResourceOwnerInventoryCatalog.ReadEntry(reverseKey, reverse, entry.Lifetime, limits);
            return;
        }
        transaction.Put(reverseKey, bytes);
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        PutOrigin(transaction, new(ResourceOwnerInventoryProtocol.Version,
            current?.StoreIncarnation ?? Guid.Empty, current?.Proven ?? false, Next(current)), limits);
    }

    internal static void Enroll(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime,
        PartitionRef partition, DatabaseLimits limits)
    {
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        if (current is null || !current.Proven)
        {
            return;
        }
        var entry = new ResourceOwnerInventoryEnrollmentV1(ResourceOwnerInventoryProtocol.Version,
            lifetime, partition, current);
        var key = ResourceOwnerInventoryKeys.Partition(entry);
        var previous = transaction.ReadOwnedValue(key);
        if (previous is not null)
        {
            entry = ResourceOwnerInventoryCatalog.ReadPartitionEntry(key, previous, partition, limits);
            if (entry.Lifetime != lifetime)
            {
                throw ResourceOwnerInventoryValidation.Invalid();
            }
        }
        Import(transaction, entry, limits);
    }

    private static long Next(ResourceOwnerInventoryOriginV1? current)
    {
        if (current?.Revision == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
        }
        return (current?.Revision ?? ResourceOwnerInventoryProtocol.Zero) + ResourceOwnerInventoryProtocol.Next;
    }

    private static void PutOrigin(IAtomicTransaction transaction, ResourceOwnerInventoryOriginV1 origin,
        DatabaseLimits limits) => transaction.Put(ResourceOwnerInventoryKeys.Origin(),
            ResourceOwnerInventoryValidation.Encode(origin, limits));
}
