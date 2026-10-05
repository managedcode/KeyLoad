using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPlanFactory
{
    private const int Version = 1;
    private const string InvalidPartitionsMessage = "The partition query leaf set is invalid.";
    private const string GrantBudgetMessage = "The partition query grants exceed the configured budget.";

    internal static PartitionQueryPlanV1 Create(AstQueryRequest request, StoreIdentity owner,
        ImmutableArray<PartitionRef> partitions, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(limits);
        var sorted = ValidateAndOrderPartitions(partitions);

        var limit = request.Query.Limit;
        var mergeReserve = PartitionQueryRetention.RootMergeReserve(sorted.Length,
            checked((long)limit * sorted.Length), limit);
        var leafFloor = checked(PartitionQueryRetention.LeafHeapReserve(limit)
            + PartitionQueryRetention.CandidateArrayBytes(limit));
        var remainingRetained = limits.MaxBatchBytes - mergeReserve;
        if (remainingRetained < checked(leafFloor * sorted.Length))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrantBudgetMessage);
        }

        var examined = Divide(limits.MaxScanRecords, sorted.Length);
        var readBytes = Divide(limits.MaxQueryReadBytes, sorted.Length);
        var retained = Divide(remainingRetained, sorted.Length);
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafPlanV1>(sorted.Length);
        for (var index = 0; index < sorted.Length; index++)
        {
            var leafRequest = request with { Partition = sorted[index], Cursor = null };
            leaves.Add(new(Version, sorted[index], leafRequest, examined[index], readBytes[index],
                retained[index], limit));
        }
        return new(Version, owner.NodeId, owner.Incarnation, owner.ReadGeneration, leaves.MoveToImmutable(),
            limit, limits.MaxScanRecords, limits.MaxQueryReadBytes, limits.MaxBatchBytes);
    }

    private static PartitionRef[] ValidateAndOrderPartitions(ImmutableArray<PartitionRef> partitions)
    {
        if (partitions.IsDefaultOrEmpty || partitions.Length > 8)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
        }

        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
            }

            DatabaseEngine.ValidatePartition(partition);
        }
        var sorted = partitions.OrderBy(static item => item.TenantId, StringComparer.Ordinal)
            .ThenBy(static item => item.DatabaseId, StringComparer.Ordinal)
            .ThenBy(static item => item.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(static item => item.PartitionKey, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            if (index > 0 && PartitionQueryOrder.ComparePartition(sorted[index - 1], sorted[index]) == 0)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
            }
        }
        return sorted;
    }

    private static int[] Divide(int total, int parts)
    {
        var quotient = total / parts;
        var remainder = total % parts;
        return Enumerable.Range(0, parts).Select(index => quotient + (index < remainder ? 1 : 0)).ToArray();
    }

    private static long[] Divide(long total, int parts)
    {
        var quotient = total / parts;
        var remainder = total % parts;
        return Enumerable.Range(0, parts).Select(index => quotient + (index < remainder ? 1 : 0)).ToArray();
    }
}
