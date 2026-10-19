using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Creates the single execution grain key for a complete, case-sensitive logical partition.</summary>
internal static class PartitionQueryGrainIdentity
{
    internal const int InterfaceVersion = 1;

    internal static string For(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }
}
