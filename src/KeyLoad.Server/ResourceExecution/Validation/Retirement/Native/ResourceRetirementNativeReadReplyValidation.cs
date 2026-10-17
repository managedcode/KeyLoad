using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Validates the closed native retirement read reply without changing its ordered result checks.</summary>
internal static class ResourceRetirementNativeReadReplyValidation
{
    private const int Empty = 0;

    internal static void Require(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
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
        RequireRemaining(query, reply, limits);
    }

    private static void RequireRemaining(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
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
