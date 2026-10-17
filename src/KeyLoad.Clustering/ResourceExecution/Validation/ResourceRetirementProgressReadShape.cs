using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementProgressReadShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.Control is not { } control || query.Request is null || control.Request != query.Request
            || query.InventoryId != Guid.Empty || query.PageSize != ResourceRetirementReadContract.NoRecords
            || query.Attempt is not null || query.AttemptId != Guid.Empty || query.Preparation is not null
            || query.Context is not null || query.WorkFence is not null || query.AcknowledgedWork is not null
            || query.ParentLookup is not null || query.SourceSeal is not null
            || query.Operation == ResourceRetirementReadOperation.OwnerRelease && query.CleanupPartition is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        ResourceRetirementValidation.Control(control, limits);
        if (query.CleanupPartition is { } partition)
        {
            DatabaseEngine.ValidatePartition(partition);
            if (partition.TenantId != control.Lifetime.TenantId || partition.DatabaseId != control.Lifetime.DatabaseId
                || partition.TransactionDomainId != control.Lifetime.TransactionDomainId)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementReadScope.InvalidRequest);
            }
        }
    }
}
