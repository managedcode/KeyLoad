using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementCleanupIssueShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.Context is not { Purpose: ResourceRetirementAttemptPurpose.CleanupPage } context
            || query.CleanupPage is not { Attempt: { } attempt } body
            || body.SealedState is null || body.SealedState.Control is null || body.Partition is null
            || !PhysicalOwnerEntryValidation.Valid(body.Receiver)
            || !PhysicalOwnerEntryValidation.Valid(query.ObservedOwner)
            || query.Request is null || query.Control is not null || query.Preparation is not null
            || query.InventoryId != Guid.Empty || query.PageSize != ResourceRetirementReadContract.NoRecords
            || query.AttemptId != Guid.Empty || query.Attempt is null || query.SourceSeal is not null
            || query.WorkFence is not null || query.AcknowledgedWork is not null || query.ParentLookup is not null
            || query.CleanupPartition is not null || query.ExpiresAt > attempt.ExpiresAt
            || !ResourceRetirementValidation.Same(attempt, query.Attempt)
            || !ResourceRetirementValidation.Same(body.SealedState.Control, context.Retirement)
            || !ResourceRetirementValidation.Same(body.Receiver, context.Receiver)
            || context.Page is null || context.Page.Partition != body.Partition
            || context.Page.Ordinal != (body.Previous is null ? ResourceRetirementProtocol.First
                : checked(body.Previous.Ordinal + ResourceRetirementProtocol.First))
            || !PhysicalOwnerEntryValidation.Same(body.Receiver, query.ObservedOwner)
            || ResourceRetirementCleanupPageValidation.Require(body, limits) != attempt.OriginalCommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        ResourceRetirementAttemptValidation.Witness(attempt, context, limits);
        if (!ResourceRetirementValidation.Same(ResourceRetirementAttemptContextValidation.Request(context), query.Request))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
    }
}
