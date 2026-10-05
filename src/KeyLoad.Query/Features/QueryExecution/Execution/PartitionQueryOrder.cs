namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryOrder
{
    internal static int ComparePartition(PartitionRef left, PartitionRef right)
    {
        var result = string.CompareOrdinal(left.TenantId, right.TenantId);
        if (result == 0)
        {
            result = string.CompareOrdinal(left.DatabaseId, right.DatabaseId);
        }

        if (result == 0)
        {
            result = string.CompareOrdinal(left.TransactionDomainId, right.TransactionDomainId);
        }

        if (result == 0)
        {
            result = string.CompareOrdinal(left.PartitionKey, right.PartitionKey);
        }

        return result;
    }

    internal static int CompareReference(EntityRef left, EntityRef right)
    {
        var result = ComparePartition(left.Partition, right.Partition);
        if (result == 0)
        {
            result = string.CompareOrdinal(left.Collection, right.Collection);
        }

        if (result == 0)
        {
            result = string.CompareOrdinal(left.Id, right.Id);
        }

        return result;
    }

    internal static int Compare(PartitionQueryCandidateV1 left, PartitionQueryCandidateV1 right,
        IReadOnlyList<Ordering> order)
    {
        for (var index = 0; index < order.Count; index++)
        {
            var result = left.OrderKeys[index].AsSpan().SequenceCompareTo(right.OrderKeys[index].AsSpan());
            if (result != 0)
            {
                return order[index].Descending ? -Math.Sign(result) : Math.Sign(result);
            }
        }
        return CompareReference(left.Reference, right.Reference);
    }
}
