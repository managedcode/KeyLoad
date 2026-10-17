using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementDropParentReadShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.Request is not null || query.ParentLookup is null || query.Control is not null
            || query.Preparation is not null || query.Context is not null || query.Attempt is not null
            || query.AttemptId != Guid.Empty || query.InventoryId != Guid.Empty || query.PageSize != ResourceRetirementReadContract.NoRecords
            || query.WorkFence is not null || query.AcknowledgedWork is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        ResourceRetirementDropParentStorage.RequireLookup(query.ParentLookup, limits);
    }
}
