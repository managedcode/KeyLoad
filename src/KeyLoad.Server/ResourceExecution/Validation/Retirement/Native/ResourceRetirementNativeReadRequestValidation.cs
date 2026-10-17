using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Validates the closed native retirement read request without changing its ordered admission checks.</summary>
internal static class ResourceRetirementNativeReadRequestValidation
{
    private const long UnassignedEpoch = 0;
    private const int Empty = 0;

    internal static void Require(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (outer.Command || query is null || query.Version != outer.Version
            || query.ExpiresAt != outer.ExpiresAt || query.OriginalPolicyEpoch != UnassignedEpoch
            || query.DestinationVoter != outer.DestinationVoter || query.DestinationSiloAddress != outer.DestinationSiloAddress
            || !PhysicalOwnerEntryValidation.Valid(query.ObservedOwner)
            || !PhysicalOwnerEntryValidation.Same(query.ObservedOwner, outer.Destination)
            || query.MaximumReadBytes != outer.MaximumReadBytes
            || query.MaximumExaminedRecords != outer.MaximumExaminedRecords || query.MaximumResultBytes != outer.MaximumResultBytes)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.IssueCleanupPage)
        {
            ResourceRetirementCleanupIssueShape.Require(query, limits);
            ResourceRetirementDurablePhaseValidation.RequireContext(outer, query.Context!, limits);
            return;
        }
        if (query.CleanupPage is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.CleanupProgress or ResourceRetirementReadOperation.OwnerRelease)
        {
            ResourceRetirementProgressReadShape.Require(query, limits);
            ResourceRetirementNativePhaseValidation.RequireControl(outer, query.Control!, limits);
            return;
        }
        if (query.CleanupPartition is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.IssueSourceSeal)
        {
            ResourceRetirementSourceSealShape.Require(query, limits);
            ResourceRetirementNativePhaseValidation.RequireControl(outer, query.Control!, limits);
            if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
            {
                throw Invalid();
            }
            return;
        }
        if (query.SourceSeal is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.DropParent)
        {
            ResourceRetirementDropParentNativeReadValidation.RequireRequest(outer, query, limits);
            return;
        }
        RequireRemaining(outer, query, limits);
    }

    private static void RequireRemaining(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        if (query.ParentLookup is not null || query.Request is null
            || query.Request.Version != outer.Version || query.Request.RetirementId == Guid.Empty)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.ActualOwnerWorkJoin)
        {
            ResourceRetirementWorkNativeReadValidation.RequireRequest(outer, query, limits);
            return;
        }
        if (query.WorkFence is not null || query.AcknowledgedWork is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.Attempt or ResourceRetirementReadOperation.PreparedOperation
            or ResourceRetirementReadOperation.IssueOwnerReservation)
        {
            ResourceRetirementDurableReadValidation.RequireRequest(outer, query, limits);
            return;
        }
        if (query.Context is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.Preparation or ResourceRetirementReadOperation.OwnerStatus)
        {
            ResourceRetirementPreparingReadValidation.RequireRequest(outer, query, limits);
            return;
        }
        if (query.Preparation is not null)
        {
            throw Invalid();
        }
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.State => query.Control is null && query.InventoryId == Guid.Empty && query.PageSize == Empty
                && query.Attempt is null && query.AttemptId == Guid.Empty,
            ResourceRetirementReadOperation.OriginalWorkSeal or ResourceRetirementReadOperation.OriginalD3Seal
                or ResourceRetirementReadOperation.Fence => query.Control is not null && query.InventoryId == Guid.Empty && query.PageSize == Empty
                && query.Attempt is null && query.AttemptId == Guid.Empty,
            ResourceRetirementReadOperation.CaptureRetiringInventory => query.Control is not null
                && query.InventoryId != Guid.Empty && query.PageSize > Empty && query.Attempt is not null
                && query.AttemptId == Guid.Empty && query.InventoryId == query.Attempt.OriginalCommandId
                && query.ExpiresAt <= query.Attempt.ExpiresAt,
            _ => false
        };
        if (!valid || query.Control is { } control && control.Request != query.Request)
        {
            throw Invalid();
        }
        if (query.Control is { } original)
        {
            ResourceRetirementNativePhaseValidation.RequireControl(outer, original, limits);
        }
        if (query.Operation != ResourceRetirementReadOperation.Fence
            && !PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
