using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryCreation
{
    internal static void Record(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime)
    {
        var domain = new PartitionRef(lifetime.TenantId, lifetime.DatabaseId,
            lifetime.TransactionDomainId, lifetime.ResourceName);
        var key = ResourceOwnerInventoryKeys.Domain(domain);
        var previous = transaction.ReadOwnedValue(key);
        var revision = previous is null ? ResourceOwnerInventoryProtocol.Zero
            : NativeSerialization.Deserialize<long>(previous);
        if (revision < ResourceOwnerInventoryProtocol.Zero || revision == long.MaxValue)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        transaction.Put(key, NativeSerialization.Serialize(revision + ResourceOwnerInventoryProtocol.Next));
        transaction.PutRecord(ResourceOwnerInventoryKeys.Creation(lifetime), lifetime);
    }

    internal static long ReadRevision(IKeyValueView view, PartitionRef partition)
    {
        var bytes = view.ReadOwnedValue(ResourceOwnerInventoryKeys.Domain(partition));
        var revision = bytes is null ? ResourceOwnerInventoryProtocol.Zero
            : NativeSerialization.Deserialize<long>(bytes);
        if (revision < ResourceOwnerInventoryProtocol.Zero)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        return revision;
    }

    internal static bool Proven(IKeyValueView view, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        var bytes = view.ReadOwnedValue(ResourceOwnerInventoryKeys.Creation(lifetime));
        if (bytes is null)
        {
            return false;
        }
        if (ResourceOwnerInventoryValidation.Decode<ResourceLifetimeV1>(bytes, limits) != lifetime)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }
        return true;
    }
}
