using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPlanValidation
{
    private const int MinimumLeafCount = 1;
    private const int MaximumLeafCount = 8;
    private const string InvalidPlanMessage = "The partition query plan is invalid.";
    private const string PlanBudgetMessage = "The partition query plan exceeds its resource budget.";

    internal static PartitionRef[] Validate(PartitionQueryPlanV1 plan, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateBounds(plan, limits);

        var partitions = new PartitionRef[plan.Leaves.Length];
        var firstRequest = NormalizeShape(plan.Leaves[0], plan, partitions, 0);
        long examined = 0;
        long bytes = 0;
        long retained = 0;
        for (var index = 0; index < plan.Leaves.Length; index++)
        {
            var leaf = plan.Leaves[index] ?? throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
            if (index != 0)
            {
                _ = NormalizeShape(leaf, plan, partitions, index);
                var comparable = leaf.Request with { Partition = firstRequest.Partition };
                if (!JsonDefaults.Serialize(comparable).AsSpan().SequenceEqual(JsonDefaults.Serialize(firstRequest)))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
                }
            }
            if (leaf.MaxCandidates != plan.Limit || leaf.MaxCandidates < 1
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

    private static void ValidateBounds(PartitionQueryPlanV1 plan, DatabaseLimits limits)
    {
        if (plan.Version != 1 || plan.NodeId == Guid.Empty || plan.Incarnation == Guid.Empty
            || plan.ReadGeneration < 0 || plan.Leaves.IsDefaultOrEmpty
            || plan.Leaves.Length is < MinimumLeafCount or > MaximumLeafCount)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
        }
        if (plan.Limit < 1 || plan.Limit > limits.MaxResults || plan.MaxExaminedRecords < 1
            || plan.MaxExaminedRecords > limits.MaxScanRecords || plan.MaxReadBytes < 0
            || plan.MaxReadBytes > limits.MaxQueryReadBytes || plan.MaxRetainedBytes < 0
            || plan.MaxRetainedBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlanBudgetMessage);
        }
    }

    private static AstQueryRequest NormalizeShape(PartitionQueryLeafPlanV1 leaf,
        PartitionQueryPlanV1 plan, PartitionRef[] partitions, int index)
    {
        if (leaf is null || leaf.Version != 1 || leaf.Partition is null || leaf.Request is null
            || leaf.Request.Partition is null || leaf.Request.Query is null
            || leaf.MaxExaminedRecords < 0 || leaf.MaxReadBytes < 0 || leaf.MaxRetainedBytes < 0
            || leaf.MaxCandidates < 0 || leaf.Request.Partition != leaf.Partition
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
        for (var index = 0; index < partitions.Length; index++)
        {
            if (!seen.Add(partitions[index])
                || index > 0 && PartitionQueryOrder.ComparePartition(partitions[index - 1], partitions[index]) >= 0)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPlanMessage);
            }
        }
    }
}
