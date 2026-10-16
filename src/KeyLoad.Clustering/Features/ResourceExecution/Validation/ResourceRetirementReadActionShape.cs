using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementReadActionShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.Operation == ResourceRetirementReadOperation.IssueCleanupPage)
        {
            ResourceRetirementCleanupIssueShape.Require(query, limits);
            return;
        }
        if (query.CleanupPage is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.CleanupProgress or ResourceRetirementReadOperation.OwnerRelease)
        {
            ResourceRetirementProgressReadShape.Require(query, limits);
            return;
        }
        if (query.CleanupPartition is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.IssueSourceSeal)
        {
            ResourceRetirementSourceSealShape.Require(query, limits);
            return;
        }
        if (query.SourceSeal is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.DropParent)
        {
            ResourceRetirementDropParentReadShape.Require(query, limits);
            return;
        }
        if (query.Request is null || query.ParentLookup is not null)
        {
            throw Invalid();
        }
        var valid = ValidAction(query, limits);
        if (query.Operation == ResourceRetirementReadOperation.ActualOwnerWorkJoin)
        {
            ResourceRetirementWorkReadShape.Require(query, limits);
        }
        else if (query.WorkFence is not null || query.AcknowledgedWork is not null)
        {
            throw Invalid();
        }
        if (!valid || query.Control is { } control && control.Request != query.Request
            || query.Preparation is { } preparation && preparation.Request != query.Request)
        {
            throw Invalid();
        }
        RequireContext(query, limits);
    }

    private static void RequireContext(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.Context is { } context)
        {
            ResourceRetirementAttemptContextValidation.Require(context, limits);
            var owner = query.Operation == ResourceRetirementReadOperation.Attempt
                ? ResourceRetirementAttemptContextValidation.SourceOwner(context)
                : ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
            if (!ResourceRetirementValidation.Same(ResourceRetirementAttemptContextValidation.Request(context), query.Request)
                || !PhysicalOwnerEntryValidation.SameOwner(owner, query.ObservedOwner.Owner))
            {
                throw Invalid();
            }
        }
    }

    private static bool ValidAction(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        var empty = query.InventoryId == Guid.Empty && query.PageSize == ResourceRetirementReadContract.NoRecords;
        var noAttempt = query.Attempt is null && query.AttemptId == Guid.Empty;
        return query.Operation switch
        {
            ResourceRetirementReadOperation.State => empty && noAttempt && query.Control is null
                && query.Preparation is null && query.Context is null,
            ResourceRetirementReadOperation.ActualOwnerWorkJoin => empty && noAttempt && query.Control is not null
                && query.Preparation is null && query.Context is null,
            ResourceRetirementReadOperation.OriginalWorkSeal or ResourceRetirementReadOperation.OriginalD3Seal
                or ResourceRetirementReadOperation.Fence => empty && noAttempt && query.Control is not null
                && query.Preparation is null && query.Context is null,
            ResourceRetirementReadOperation.CaptureRetiringInventory => query.Control is not null
                && query.Attempt is not null && query.AttemptId == Guid.Empty && query.Context is null
                && query.Preparation is null && query.InventoryId == query.Attempt.OriginalCommandId
                && query.ExpiresAt <= query.Attempt.ExpiresAt && query.InventoryId != Guid.Empty
                && query.PageSize > ResourceRetirementReadContract.NoRecords && query.PageSize <= limits.MaxResults,
            ResourceRetirementReadOperation.Attempt => empty && query.Context is not null
                && query.Control is null && query.Preparation is null && query.Attempt is null && (query.AttemptId != Guid.Empty
                    || query.Context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage),
            ResourceRetirementReadOperation.PreparedOperation => empty && query.Context is not null
                && query.Control is null && query.Preparation is null && query.Attempt is not null && query.AttemptId == Guid.Empty,
            ResourceRetirementReadOperation.Preparation => empty && noAttempt && query.Control is null
                && query.Preparation is null && query.Context is null,
            ResourceRetirementReadOperation.OwnerStatus => empty && noAttempt && query.Control is null
                && query.Preparation is not null && query.Context is null,
            ResourceRetirementReadOperation.IssueOwnerReservation => empty && query.Control is null
                && query.Preparation is null && query.Attempt is not null && query.AttemptId == Guid.Empty
                && query.Context?.Purpose == ResourceRetirementAttemptPurpose.OwnerReservation
                && query.ExpiresAt <= query.Attempt.ExpiresAt,
            _ => false
        };
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
}
