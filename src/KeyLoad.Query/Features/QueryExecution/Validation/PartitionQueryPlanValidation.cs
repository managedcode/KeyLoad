using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPlanValidation
{
    private const int FirstElementIndex = 0;
    private const int EmptyElementCount = 0;
    private const int InitialSequence = 0;
    private const int NoRetainedBytes = 0;
    private const int MinimumPositiveCount = 1;
    private const int VersionOne = 1;
    private const int AdjacentElementOffset = 1;
    private const int EqualOrder = 0;

    private const int MinimumLeafCount = 1;
    private const string InvalidPlanMessage = "The partition query plan is invalid.";
    private const string PlanBudgetMessage = "The partition query plan exceeds its resource budget.";

    internal static PartitionRef[] Validate(PartitionQueryPlanV1 plan, DatabaseLimits limits, QueryExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateBounds(plan, limits, execution.MaximumPartitions);
        if (plan.MaxRetainedBytes > QueryResultBudgetPolicy.Resolve(limits, execution))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlanBudgetMessage);
        }

        var partitions = new PartitionRef[plan.Leaves.Length];
        var firstRequest = NormalizeShape(plan.Leaves[FirstElementIndex], plan, partitions, EmptyElementCount);
        long examined = InitialSequence;
        long bytes = NoRetainedBytes;
        long retained = NoRetainedBytes;
        for (var index = FirstElementIndex; index < plan.Leaves.Length; index++)
        {
            var leaf = plan.Leaves[index] ?? throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
            if (index != EmptyElementCount)
            {
                _ = NormalizeShape(leaf, plan, partitions, index);
                var comparable = leaf.Request with { Partition = firstRequest.Partition };
                if (!JsonDefaults.Serialize(comparable).AsSpan().SequenceEqual(JsonDefaults.Serialize(firstRequest)))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
                }
            }
            if (leaf.MaxCandidates != plan.Limit || leaf.MaxCandidates < MinimumPositiveCount
                || leaf.MaxRetainedBytes < checked(PartitionQueryRetention.LeafHeapReserve(plan.Limit)
                    + PartitionQueryRetention.CandidateArrayBytes(plan.Limit))
                || leaf.MaxExaminedRecords > plan.MaxExaminedRecords - examined
                || leaf.MaxReadBytes > plan.MaxReadBytes - bytes
                || leaf.MaxRetainedBytes > plan.MaxRetainedBytes - retained)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, PlanBudgetMessage);
            }

            examined += leaf.MaxExaminedRecords;
            bytes += leaf.MaxReadBytes;
            retained += leaf.MaxRetainedBytes;
        }

        EnsureUniqueAndOrdered(partitions);
        var mergeReserve = PartitionQueryRetention.RootMergeReserve(plan);
        if (mergeReserve > plan.MaxRetainedBytes || retained > plan.MaxRetainedBytes - mergeReserve)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlanBudgetMessage);
        }

        return partitions;
    }

    private static void ValidateBounds(PartitionQueryPlanV1 plan, DatabaseLimits limits, int maximumLeafCount)
    {
        if (plan.Version != VersionOne || plan.NodeId == Guid.Empty || plan.Incarnation == Guid.Empty
            || plan.ReadGeneration < InitialSequence || plan.Leaves.IsDefaultOrEmpty
            || plan.Leaves.Length < MinimumLeafCount || plan.Leaves.Length > maximumLeafCount)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
        }
        if (plan.Limit < MinimumPositiveCount || plan.Limit > limits.MaxResults || plan.MaxExaminedRecords < MinimumPositiveCount
            || plan.MaxExaminedRecords > limits.MaxScanRecords || plan.MaxReadBytes < NoRetainedBytes
            || plan.MaxReadBytes > limits.MaxQueryReadBytes || plan.MaxRetainedBytes < NoRetainedBytes
            || plan.MaxRetainedBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlanBudgetMessage);
        }
    }

    private static AstQueryRequest NormalizeShape(PartitionQueryLeafPlanV1 leaf,
        PartitionQueryPlanV1 plan, PartitionRef[] partitions, int index)
    {
        if (leaf is null || leaf.Version != VersionOne || leaf.Partition is null || leaf.Request is null
            || leaf.Request.Partition is null || leaf.Request.Query is null
            || leaf.MaxExaminedRecords < EmptyElementCount || leaf.MaxReadBytes < NoRetainedBytes || leaf.MaxRetainedBytes < NoRetainedBytes
            || leaf.MaxCandidates < NoRetainedBytes || leaf.Request.Partition != leaf.Partition
            || leaf.Request.Query.Limit != plan.Limit || leaf.Request.Cursor is not null
            || leaf.Request.Query.Explain || leaf.Request.Query.ModelSource is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
        }
        DatabaseEngine.ValidatePartition(leaf.Partition);
        partitions[index] = leaf.Partition;
        return leaf.Request;
    }

    private static void EnsureUniqueAndOrdered(PartitionRef[] partitions)
    {
        var seen = new HashSet<PartitionRef>();
        for (var index = FirstElementIndex; index < partitions.Length; index++)
        {
            if (!seen.Add(partitions[index])
                || index > EmptyElementCount && PartitionQueryOrder.ComparePartition(partitions[index - AdjacentElementOffset], partitions[index]) >= EqualOrder)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
            }
        }
    }
}
