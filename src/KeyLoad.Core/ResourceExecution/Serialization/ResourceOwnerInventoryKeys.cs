using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryKeys
{
    internal static byte[] Origin() => KeyCodec.Encode(ResourceOwnerInventoryProtocol.OriginSpace);
    internal static byte[] Prefix(ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(ResourceOwnerInventoryProtocol.EntrySpace, lifetime.TenantId,
            lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value);
    internal static byte[] Entry(ResourceLifetimeV1 lifetime, PartitionRef partition)
        => KeyCodec.Encode(ResourceOwnerInventoryProtocol.EntrySpace, lifetime.TenantId,
            lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value,
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey);
    internal static byte[] Partition(ResourceOwnerInventoryEnrollmentV1 entry)
        => KeySpace.Partition(PartitionRecordFamilies.ResourceOwnerInventoryMember,
            entry.Partition, entry.Lifetime.ResourceName, entry.Lifetime.Incarnation.Value);
    internal static byte[] Domain(PartitionRef partition)
        => KeyCodec.Encode(ResourceOwnerInventoryProtocol.DomainSpace, partition.TenantId,
            partition.DatabaseId, partition.TransactionDomainId);
    internal static byte[] Creation(ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(ResourceOwnerInventoryProtocol.CreationSpace, lifetime.TenantId,
            lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value);
    internal static byte[] Witness(PartitionRef partition)
        => KeyCodec.Encode(ResourceOwnerInventoryProtocol.WitnessSpace, partition.TenantId,
            partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey);
}
