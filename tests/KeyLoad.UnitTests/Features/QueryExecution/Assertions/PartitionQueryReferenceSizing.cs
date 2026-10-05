using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryReferenceSizing
{
    private const long Pointer = 8;
    private const long ArrayHeader = 32;
    private const long StringHeader = 32;
    private const long CandidateHeader = 256;
    private const long HeapSlot = 64;
    private const long ReferenceSlot = 64;
    private const long RootHeader = 128;
    private const long LeafHeader = 256;

    internal static long MinimumSingleLeafBatchBytes(PartitionQueryResultV1 result, int limit)
    {
        if (result.Leaves.Length != 1)
        { throw new InvalidOperationException(); }
        var leaf = result.Leaves[0];
        var leafBytes = ArrayBytes(leaf.Candidates.Length);
        foreach (var candidate in leaf.Candidates)
        { leafBytes = checked(leafBytes + CandidateBytes(candidate)); }
        var mergeReserve = checked(RootHeader + LeafHeader + ReferenceSlot * limit + HeapSlot + ArrayBytes(limit));
        var leafPeak = checked(ArrayBytes(limit + 1) + leafBytes);
        return checked(mergeReserve + leafPeak);
    }

    internal static long RetainedBytes(PartitionQueryResultV1 result)
    {
        var bytes = checked(RootHeader + (long)LeafHeader * result.Leaves.Length
            + (long)HeapSlot * result.Leaves.Length + ArrayBytes(result.Rows.Length));
        var candidateCount = 0;
        foreach (var leaf in result.Leaves)
        {
            bytes = checked(bytes + ArrayBytes(leaf.Candidates.Length));
            foreach (var candidate in leaf.Candidates)
            {
                candidateCount++;
                bytes = checked(bytes + CandidateBytes(candidate));
            }
        }
        return checked(bytes + (long)ReferenceSlot * candidateCount);
    }

    private static long CandidateBytes(PartitionQueryCandidateV1 candidate)
    {
        var reference = candidate.Reference;
        var bytes = CandidateHeader;
        bytes = AddString(bytes, reference.Partition.TenantId);
        bytes = AddString(bytes, reference.Partition.DatabaseId);
        bytes = AddString(bytes, reference.Partition.TransactionDomainId);
        bytes = AddString(bytes, reference.Partition.PartitionKey);
        bytes = AddString(bytes, reference.Collection);
        bytes = AddString(bytes, reference.Id);
        bytes = AddString(bytes, candidate.Row.Json);
        var redacted = candidate.Row.RedactedFields?.Length ?? 0;
        bytes = checked(bytes + ArrayBytes(redacted));
        if (candidate.Row.RedactedFields is { } fields)
        {
            foreach (var field in fields)
            {
                bytes = AddString(bytes, field);
            }
        }
        bytes = checked(bytes + ArrayBytes(candidate.OrderKeys.Length));
        foreach (var key in candidate.OrderKeys)
        {
            bytes = checked(bytes + ArrayHeader + key.Length);
        }

        return bytes;
    }

    private static long ArrayBytes(int length) => checked(ArrayHeader + Pointer * length);

    private static long AddString(long current, string value)
        => checked(current + StringHeader + sizeof(char) * (long)value.Length);
}
