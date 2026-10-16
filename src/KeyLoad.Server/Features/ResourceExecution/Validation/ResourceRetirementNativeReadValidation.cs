using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementNativeReadValidation
{
    private const long UnassignedEpoch = 0;
    private const int Empty = 0;

    internal static void RequireRequest(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
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

    internal static void RequireReply(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply is null || reply.ReadBytes < Empty || reply.ReadBytes > query.MaximumReadBytes
            || reply.ExaminedRecords < Empty || reply.ExaminedRecords > query.MaximumExaminedRecords)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.IssueCleanupPage)
        {
            ResourceRetirementCleanupIssueReply.Require(query, reply, limits);
            return;
        }
        if (query.Operation is ResourceRetirementReadOperation.CleanupProgress or ResourceRetirementReadOperation.OwnerRelease)
        {
            ResourceRetirementProgressReply.Require(query, reply, limits);
            return;
        }
        if (reply.CleanupCheckpoint is not null || reply.OwnerCleanup is not null || reply.OwnerRelease is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.IssueSourceSeal)
        {
            ResourceRetirementSourceSealReply.Require(query, reply, limits);
            return;
        }
        if (query.Operation == ResourceRetirementReadOperation.DropParent)
        {
            ResourceRetirementDropParentNativeReadValidation.RequireReply(query, reply, limits);
            return;
        }
        if (reply.Parent is not null || reply.AdmittedDrop is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.ActualOwnerWorkJoin)
        {
            ResourceRetirementWorkNativeReadValidation.RequireReply(query, reply, limits);
            return;
        }
        if (reply.WorkJoinWitness is not null || reply.AcknowledgedOwner is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.Attempt or ResourceRetirementReadOperation.PreparedOperation
            or ResourceRetirementReadOperation.IssueOwnerReservation)
        {
            ResourceRetirementDurableReadValidation.RequireReply(query, reply, limits);
            return;
        }
        if (reply.OriginalOperation is not null)
        {
            throw Invalid();
        }
        if (query.Operation is ResourceRetirementReadOperation.Preparation or ResourceRetirementReadOperation.OwnerStatus)
        {
            ResourceRetirementPreparingReadValidation.RequireReply(query, reply);
            return;
        }
        if (reply.Preparation is not null || reply.OwnerStatus is not null)
        {
            throw Invalid();
        }
        var absent = reply.State is null && reply.Fence is null && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null;
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.State => absent || reply.State is { Control: { } control } && control.Request == query.Request
                && reply.Fence is null && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.OriginalWorkSeal or ResourceRetirementReadOperation.OriginalD3Seal =>
                reply.State is { } saved && ResourceRetirementValidation.Same(saved.Control, query.Control)
                && reply.Fence is null && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.Fence => absent || reply.Fence is { Control: { } control } && control.Request == query.Request
                && reply.State is null && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.CaptureRetiringInventory => reply.InventoryControl is { } inventory
                && InventoryMatches(query, inventory) && reply.State is null && reply.Fence is null && reply.Attempt is null && reply.PreparedOperation is null,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    private static bool InventoryMatches(ResourceRetirementReadRequestV1 query, ResourceOwnerInventoryControlV1 inventory)
        => inventory.Request is { } request && request.Version == ResourceRetirementNativeProtocol.Version
            && request.InventoryId == query.InventoryId && request.TenantId == query.Request!.TenantId
            && request.DatabaseId == query.Request!.DatabaseId && request.ResourceName == query.Request!.ResourceName
            && request.ExpectedIncarnation == query.Request!.ExpectedIncarnation && request.PageSize == query.PageSize
            && request.ExpiresAt == query.ExpiresAt && query.Attempt is not null
            && ResourceRetirementValidation.Same(inventory.RetirementAttempt, query.Attempt);

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
