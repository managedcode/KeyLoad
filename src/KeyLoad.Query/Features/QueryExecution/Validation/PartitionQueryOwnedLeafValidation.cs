using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryOwnedLeafValidation
{
    private const int Version = 1;
    private const int MinimumCount = 1;
    private const long EmptyBytes = 0;
    private const int EmptyRecords = 0;
    private const string InvalidDetail = "The partition query plan is invalid.";
    private const string BudgetDetail = "The partition query plan exceeds its resource budget.";

    internal static void Validate(PartitionQueryOwnedLeafRequest request, DatabaseLimits limits,
        QueryExecutionOptions execution)
    {
        var leaf = request.Plan;
        if (request.Owner is null || leaf is null || leaf.Version != Version || leaf.Partition is null || leaf.Request is null
            || leaf.Request.Partition != leaf.Partition || leaf.Request.Query is null
            || string.IsNullOrEmpty(request.Tenant) || leaf.Partition.TenantId != request.Tenant
            || leaf.Request.Cursor is not null || leaf.Request.Query.Explain
            || leaf.Request.Query.ModelSource is not null || leaf.MaxCandidates != leaf.Request.Query.Limit)
        { throw Errors.Fail(ErrorCode.Validation, InvalidDetail); }
        DatabaseEngine.ValidatePartition(leaf.Partition);
        if (leaf.MaxCandidates < MinimumCount || leaf.MaxCandidates > limits.MaxResults
            || leaf.MaxExaminedRecords < EmptyRecords || leaf.MaxExaminedRecords > limits.MaxScanRecords
            || leaf.MaxReadBytes < EmptyBytes || leaf.MaxReadBytes > limits.MaxQueryReadBytes
            || leaf.MaxRetainedBytes < checked(PartitionQueryRetention.LeafHeapReserve(leaf.MaxCandidates)
                + PartitionQueryRetention.CandidateArrayBytes(leaf.MaxCandidates))
            || leaf.MaxRetainedBytes > QueryResultBudgetPolicy.Resolve(limits, execution))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, BudgetDetail); }
    }
}
