using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementNativePhaseValidation
{
    private const long UnassignedEpoch = 0;

    internal static void RequireRequest(ResourceRetirementNativeRequest outer, ResourceRetirementNativePhaseRequestV1 request, DatabaseLimits limits)
    {
        if (!outer.Command || request is null || request.Version != outer.Version || request.Phase is null
            || request.Phase.Version != outer.Version || request.Phase.RetirementId == Guid.Empty
            || request.Phase.Body.IsEmpty || request.ExpiresAt != outer.ExpiresAt || request.OriginalPolicyEpoch != UnassignedEpoch
            || request.DestinationVoter != outer.DestinationVoter || request.DestinationSiloAddress != outer.DestinationSiloAddress
            || !PhysicalOwnerEntryValidation.Valid(request.ObservedOwner)
            || !PhysicalOwnerEntryValidation.Same(request.ObservedOwner, outer.Destination)
            || request.MaximumReadBytes != outer.MaximumReadBytes
            || request.MaximumExaminedRecords != outer.MaximumExaminedRecords || request.MaximumResultBytes != outer.MaximumResultBytes)
        {
            throw Invalid();
        }
        if (request.OriginalOperation is not null && request.Phase.Phase is not (ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.ReserveOwner or ResourceRetirementPhase.CleanupPage))
        {
            throw Invalid();
        }
        if ((request.Phase.Phase is ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.ReserveOwner)
            && request.OriginalOperation is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireBody(outer, request.Phase, limits);
    }

    private static void RequireBody(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        if (ResourceRetirementPhaseBodyValidation.Require(phase, limits) != outer.CommandId)
        {
            throw Invalid();
        }
        if (ResourceRetirementCompletionPhase.Handles(phase.Phase))
        {
            ResourceRetirementCompletionPhase.Require(outer, phase, limits);
            return;
        }
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.Begin:
                RequireBegin(outer, phase, limits);
                break;
            case ResourceRetirementPhase.FenceOwner:
                var fence = NativeSerialization.Deserialize<ResourceRetirementFenceBodyV1>(phase.Body.Span);
                if (fence?.Control is null || !PhysicalOwnerEntryValidation.Valid(fence.Receiver)
                    || !PhysicalOwnerEntryValidation.Same(fence.Receiver, outer.Destination))
                {
                    throw Invalid();
                }
                RequireControl(outer, fence.Control, limits);
                RequireRetirement(phase, fence.Control);
                break;
            case ResourceRetirementPhase.RecordOwnerReceipt:
                var receipt = NativeSerialization.Deserialize<ResourceRetirementReceiptBodyV1>(phase.Body.Span);
                if (receipt?.Control is null || receipt.OwnerReceipt is null)
                {
                    throw Invalid();
                }
                RequireControl(outer, receipt.Control, limits);
                RequireRetirement(phase, receipt.Control);
                RequireSource(outer);
                break;
            case ResourceRetirementPhase.SealBarrier:
                RequireSeal(outer, phase, limits);
                break;
            case ResourceRetirementPhase.PrepareAttempt:
                ResourceRetirementDurablePhaseValidation.RequirePrepare(outer, phase, limits);
                break;
            case ResourceRetirementPhase.PreparedOperation:
                ResourceRetirementDurablePhaseValidation.RequirePrepared(outer, phase, limits);
                break;
            case ResourceRetirementPhase.PrepareRetirement:
            case ResourceRetirementPhase.ReserveOwner:
            case ResourceRetirementPhase.RecordReservation:
                ResourceRetirementPreparingPhaseValidation.Require(outer, phase, limits);
                break;
            case ResourceRetirementPhase.CommitOwnerWorkJoin:
            case ResourceRetirementPhase.RecordOwnerWorkJoin:
            case ResourceRetirementPhase.SealAllWork:
                ResourceRetirementWorkPhaseValidation.Require(outer, phase, limits);
                break;
            default:
                throw Invalid();
        }
    }

    private static void RequireBegin(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var begin = NativeSerialization.Deserialize<ResourceRetirementBeginBodyV1>(phase.Body.Span);
        if (begin?.Request is null || begin.Request.RetirementId != phase.RetirementId
            || outer.CommandId != phase.RetirementId)
        {
            throw Invalid();
        }
        ResourceRetirementPreparingPhaseValidation.RequireBegin(outer, begin, limits);
        RequireSource(outer);
    }

    private static void RequireSeal(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span);
        if (body?.Control is null || body.Inventory is null || body.Attempt is null
            || body.Attempt.Stage != ResourceRetirementAttemptStage.Prepared
            || body.Attempt.AttemptId == Guid.Empty || body.Attempt.OriginalCommandId != outer.CommandId
            || body.Inventory.Control?.Request is null || body.Inventory.Control.Request.InventoryId != body.Attempt.OriginalCommandId
            || !ResourceRetirementValidation.Same(body.Attempt, body.Inventory.Control.RetirementAttempt))
        {
            throw Invalid();
        }
        RequireControl(outer, body.Control, limits);
        RequireRetirement(phase, body.Control);
        RequireSource(outer);
    }

    internal static void RequireControl(ResourceRetirementNativeRequest outer, ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Control(control, limits);
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, outer.Control.Owner))
        {
            throw Invalid();
        }
    }

    private static void RequireRetirement(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementControlV1 control)
    {
        if (control.Request.RetirementId != phase.RetirementId)
        {
            throw Invalid();
        }
    }

    private static void RequireSource(ResourceRetirementNativeRequest outer)
    {
        if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
