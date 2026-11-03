using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Creates the single execution grain key for a complete, case-sensitive logical partition.</summary>
internal static class PartitionQueryGrainIdentity
{

    internal static string For(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: BCL null validation accepts the partition PartitionRef reference through its object parameter. */;
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }
}
