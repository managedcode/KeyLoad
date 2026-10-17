using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementSourceSealShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.SourceSeal is not { Control: { } control, Attempt: { } attempt, Inventory: { Control: { } inventoryControl } inventory }
            || query.Control is null || query.Request is null || query.Attempt is null
            || query.InventoryId != attempt.OriginalCommandId || query.PageSize != ResourceRetirementReadContract.NoRecords
            || query.AttemptId != Guid.Empty || query.Context is not null || query.Preparation is not null
            || query.WorkFence is not null || query.AcknowledgedWork is not null || query.ParentLookup is not null
            || !ResourceRetirementValidation.Same(query.Control, control) || control.Request != query.Request
            || !ResourceRetirementValidation.Same(query.Attempt, attempt)
            || !ResourceRetirementValidation.Same(inventoryControl.RetirementAttempt, attempt)
            || query.ExpiresAt > attempt.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        ResourceRetirementValidation.Control(control, limits);
        ResourceRetirementAttemptValidation.Witness(attempt,
            new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.SealBarrier, control, null, null), limits);
        ResourceRetirementInventoryValidation.Require(inventory, control, limits);
    }
}
