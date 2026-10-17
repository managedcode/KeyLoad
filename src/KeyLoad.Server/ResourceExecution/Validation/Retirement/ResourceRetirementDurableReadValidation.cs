using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementDurableReadValidation
{
    private const int NoPageSize = 0;

    internal static void RequireRequest(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query,
        DatabaseLimits limits)
    {
        if (query.Control is not null || query.Preparation is not null || query.InventoryId != Guid.Empty || query.PageSize != NoPageSize
            || query.Context is not { } context)
        {
            throw Invalid();
        }
        ResourceRetirementDurablePhaseValidation.RequireContext(outer, context, limits);
        if (!ResourceRetirementValidation.Same(ResourceRetirementAttemptContextValidation.Request(context), query.Request))
        {
            throw Invalid();
        }
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.Attempt => query.Attempt is null && (query.AttemptId != Guid.Empty
                    || context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
                && PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination),
            ResourceRetirementReadOperation.PreparedOperation => query.Attempt is not null && query.AttemptId == Guid.Empty
                && (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier
                    ? PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination)
                    : PhysicalOwnerEntryValidation.Same(context.Receiver!, outer.Destination)),
            ResourceRetirementReadOperation.IssueOwnerReservation => query.Attempt is not null && query.AttemptId == Guid.Empty
                && context.Purpose == ResourceRetirementAttemptPurpose.OwnerReservation
                && context.Receiver is not null && PhysicalOwnerEntryValidation.Same(context.Receiver, outer.Destination)
                && query.ExpiresAt <= query.Attempt.ExpiresAt,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
        if (query.Attempt is { } attempt)
        {
            if (query.Operation == ResourceRetirementReadOperation.IssueOwnerReservation)
            {
                KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation.Witness(attempt, context, limits);
            }
            else
            {
                KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation.Require(attempt, context, limits);
            }
        }
    }

    internal static void RequireReply(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Preparation is not null || reply.OwnerStatus is not null)
        {
            throw Invalid();
        }
        var absent = reply.Attempt is null && reply.PreparedOperation is null && reply.OriginalOperation is null;
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.Attempt => (absent && query.AttemptId == Guid.Empty && query.Context?.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
                || reply.Attempt is { } attempt && (attempt.AttemptId == query.AttemptId
                    || query.AttemptId == Guid.Empty && query.Context?.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
                && attempt.PrepareReceipt is { } receipt && receipt.RetirementId == query.Request!.RetirementId
                && receipt.Phase == ResourceRetirementPhase.PrepareAttempt
                && reply.PreparedOperation is null && reply.OriginalOperation is null,
            ResourceRetirementReadOperation.PreparedOperation => absent || reply.PreparedOperation is { } prepared
                && query.Context is not null && query.Attempt is not null
                && ResourceRetirementPreparedReplyValidation.Matches(query.Context, query.Attempt, prepared, limits)
                && reply.Attempt is null && reply.OriginalOperation is null,
            ResourceRetirementReadOperation.IssueOwnerReservation => reply.OriginalOperation is { } original
                && OriginalMatches(query, original) && reply.Attempt is null && reply.PreparedOperation is null,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.Attempt && reply.Attempt is not null)
        {
            KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation.Require(reply.Attempt!, query.Context!, limits);
        }
    }

    private static bool OriginalMatches(ResourceRetirementReadRequestV1 query, ReplicatedOperation original)
    {
        if (query.Context is null || query.Attempt is null || original.Kind != OperationKind.ResourceRetirementPhase
            || original.NativePayload.IsEmpty || original.Id != query.Attempt.OriginalCommandId)
        {
            return false;
        }
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        if (phase.Phase != ResourceRetirementPhase.ReserveOwner || phase.RetirementId != query.Request!.RetirementId)
        {
            return false;
        }
        var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
        return body is not null && ResourceRetirementValidation.Same(body.Context, query.Context)
            && ResourceRetirementValidation.Same(body.Attempt, query.Attempt);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
