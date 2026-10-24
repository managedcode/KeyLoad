using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryOrder
{
    private const int EqualOrder = 0;
    private const int FirstElementIndex = 0;
    private const int BeforeOrder = -1;
    private const int AfterOrder = 1;
    private static readonly byte[] NullKey = KeyCodec.Encode((object?)null);

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

    internal static int Compare(PartitionQueryCandidate left, PartitionQueryCandidate right,
        IReadOnlyList<Ordering> order)
    {
        for (var index = FirstElementIndex; index < order.Count; index++)
        {
            var result = CompareKey(left.OrderKeys[index].AsSpan(), right.OrderKeys[index].AsSpan(), order[index]);
            if (result != EqualOrder)
            {
                return result;
            }
        }
        return CompareReference(left.Reference, right.Reference);
    }

    internal static int Compare(PreparedRow left, PreparedRow right, ImmutableArray<Ordering> order,
        bool ordinalFullReferenceOrder)
    {
        for (var index = FirstElementIndex; index < order.Length; index++)
        {
            var result = CompareKey(GetOrderKey(left, index), GetOrderKey(right, index), order[index]);
            if (result != EqualOrder)
            {
                return result;
            }
        }
        if (ordinalFullReferenceOrder)
        {
            return PartitionQueryOrder.CompareReference(left.Reference, right.Reference);
        }

        return left.IdKey!.AsSpan().SequenceCompareTo(right.IdKey!);
    }

    private static int CompareKey(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, Ordering order)
    {
        if (order.NullsFirst is { } first)
        {
            var leftNull = left.SequenceEqual(NullKey);
            var rightNull = right.SequenceEqual(NullKey);
            if (leftNull != rightNull)
            {
                return leftNull == first ? BeforeOrder : AfterOrder;
            }
        }
        var comparison = left.SequenceCompareTo(right);
        return order.Descending ? -Math.Sign(comparison) : Math.Sign(comparison);
    }

    private static ReadOnlySpan<byte> GetOrderKey(PreparedRow row, int index) =>
        row.Candidate is { } candidate ? candidate.OrderKeys[index].AsSpan() : row.PendingOrderKeys![index];
}
