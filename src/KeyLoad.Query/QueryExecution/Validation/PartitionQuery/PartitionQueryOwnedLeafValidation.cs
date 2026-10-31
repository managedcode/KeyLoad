using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryOwnedLeafValidation
{
    private const int Version = 1;
    private const int MinimumCount = 1;
    private const string InvalidDetail = "The partition query plan is invalid.";

    internal static void Validate(PartitionQueryOwnedLeafRequest request)
    {
        var leaf = request.Plan;
        if (request.Owner is null || leaf is null || leaf.Version != Version || leaf.Partition is null || leaf.Request is null
            || leaf.Request.Partition != leaf.Partition || leaf.Request.Query is null
            || string.IsNullOrEmpty(request.Tenant) || request.Tenant != leaf.Partition.TenantId
            || leaf.Request.Cursor is not null || leaf.Request.Query.Explain
            || leaf.Request.Query.ModelSource is not null || leaf.MaxCandidates != leaf.Request.Query.Limit
            || leaf.MaxCandidates < MinimumCount)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDetail);
        }
        EngineResourceAccess.ValidatePartition(leaf.Partition);
        JsonData.Identifier(request.Tenant);
    }
}
