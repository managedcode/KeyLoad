using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryOrder
{
    private const int EqualOrder = 0;
    private const int FirstElementIndex = 0;

    internal static int ComparePartition(PartitionRef left, PartitionRef right)
    {
        var result = string.CompareOrdinal(left.TenantId, right.TenantId);
        if (result == EqualOrder)
        {
            result = string.CompareOrdinal(left.DatabaseId, right.DatabaseId);
        }

        if (result == EqualOrder)
        {
            result = string.CompareOrdinal(left.TransactionDomainId, right.TransactionDomainId);
        }

        if (result == EqualOrder)
        {
            result = string.CompareOrdinal(left.PartitionKey, right.PartitionKey);
        }

        return result;
    }

    internal static int CompareReference(EntityRef left, EntityRef right)
    {
        var result = ComparePartition(left.Partition, right.Partition);
        if (result == EqualOrder)
        {
            result = string.CompareOrdinal(left.Collection, right.Collection);
        }

        if (result == EqualOrder)
        {
            result = string.CompareOrdinal(left.Id, right.Id);
        }

        return result;
    }

    internal static int Compare(PartitionQueryCandidateV1 left, PartitionQueryCandidateV1 right,
        IReadOnlyList<Ordering> order)
    {
        for (var index = FirstElementIndex; index < order.Count; index++)
        {
            var result = left.OrderKeys[index].AsSpan().SequenceCompareTo(right.OrderKeys[index].AsSpan());
            if (result != EqualOrder)
            {
                return order[index].Descending ? -Math.Sign(result) : Math.Sign(result);
            }
        }
        return CompareReference(left.Reference, right.Reference);
    }

    internal static int Compare(PreparedRow left, PreparedRow right, ImmutableArray<Ordering> order,
        bool ordinalFullReferenceOrder)
    {
        for (var index = FirstElementIndex; index < order.Length; index++)
        {
            var result = GetOrderKey(left, index).SequenceCompareTo(GetOrderKey(right, index));
            if (result != EqualOrder)
            {
                return order[index].Descending ? -Math.Sign(result) : Math.Sign(result);
            }
        }
        if (ordinalFullReferenceOrder)
        {
            return PartitionQueryOrder.CompareReference(left.Reference, right.Reference);
        }

        return left.IdKey!.AsSpan().SequenceCompareTo(right.IdKey!);
    }

    private static ReadOnlySpan<byte> GetOrderKey(PreparedRow row, int index) =>
        row.Candidate is { } candidate ? candidate.OrderKeys[index].AsSpan() : row.PendingOrderKeys![index];
}
