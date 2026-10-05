using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryRetention
{
    internal const long PointerBytes = 8;
    internal const long ArrayDescriptorBytes = 32;
    internal const long StringDescriptorBytes = 32;
    internal const long CandidateDescriptorBytes = 256;
    internal const long HeapEntryBytes = 64;
    internal const long SeenReferenceEntryBytes = 64;
    internal const long RootDescriptorBytes = 128;
    internal const long LeafDescriptorBytes = 256;

    internal static long CandidateBytes(PartitionQueryCandidateV1 candidate)
        => CandidateBytes(candidate.Reference, candidate.Row, candidate.OrderKeys);

    internal static long CandidateBytes(EntityRef reference, QueryRow row,
        IReadOnlyList<ImmutableArray<byte>> orderKeys)
    {
        var bytes = CandidateDescriptorBytes;
        bytes = AddString(bytes, reference.Partition.TenantId);
        bytes = AddString(bytes, reference.Partition.DatabaseId);
        bytes = AddString(bytes, reference.Partition.TransactionDomainId);
        bytes = AddString(bytes, reference.Partition.PartitionKey);
        bytes = AddString(bytes, reference.Collection);
        bytes = AddString(bytes, reference.Id);
        bytes = AddString(bytes, row.Json);
        var redacted = row.RedactedFields?.Length ?? 0;
        bytes = checked(bytes + StringDescriptorBytes + (long)PointerBytes * redacted);
        if (row.RedactedFields is { } fields)
        {
            foreach (var field in fields)
            {
                bytes = AddString(bytes, field);
            }
        }
        bytes = checked(bytes + ArrayDescriptorBytes + (long)PointerBytes * orderKeys.Count);
        foreach (var key in orderKeys)
        {
            bytes = checked(bytes + ArrayDescriptorBytes + key.Length);
        }

        return bytes;
    }

    internal static long CandidateBytes(EntityRef reference, QueryRow row, byte[][] orderKeys)
    {
        var bytes = CandidateDescriptorBytes;
        bytes = AddString(bytes, reference.Partition.TenantId);
        bytes = AddString(bytes, reference.Partition.DatabaseId);
        bytes = AddString(bytes, reference.Partition.TransactionDomainId);
        bytes = AddString(bytes, reference.Partition.PartitionKey);
        bytes = AddString(bytes, reference.Collection);
        bytes = AddString(bytes, reference.Id);
        bytes = AddString(bytes, row.Json);
        var redacted = row.RedactedFields?.Length ?? 0;
        bytes = checked(bytes + StringDescriptorBytes + (long)PointerBytes * redacted);
        if (row.RedactedFields is { } fields)
        {
            foreach (var field in fields)
            {
                bytes = AddString(bytes, field);
            }
        }
        bytes = checked(bytes + ArrayDescriptorBytes + (long)PointerBytes * orderKeys.Length);
        foreach (var key in orderKeys)
        {
            bytes = checked(bytes + ArrayDescriptorBytes + key.Length);
        }

        return bytes;
    }

    internal static long RootMergeReserve(PartitionQueryPlanV1 plan)
    {
        long candidates = 0;
        foreach (var leaf in plan.Leaves)
        {
            candidates = checked(candidates + leaf.MaxCandidates);
        }

        return RootMergeReserve(plan.Leaves.Length, candidates, plan.Limit);
    }

    internal static long RootMergeReserve(int leafCount, long maximumCandidates, int resultLimit)
        => checked(RootDescriptorBytes + (long)LeafDescriptorBytes * leafCount
            + (long)SeenReferenceEntryBytes * maximumCandidates + (long)HeapEntryBytes * leafCount
            + CandidateArrayBytes(resultLimit));

    internal static long LeafHeapReserve(int limit)
        => checked(ArrayDescriptorBytes + (long)PointerBytes * (limit + 1));

    internal static long CandidateArrayBytes(int count)
        => checked(ArrayDescriptorBytes + (long)PointerBytes * count);

    private static long AddString(long bytes, string value)
        => checked(bytes + StringDescriptorBytes + (long)sizeof(char) * value.Length);
}
