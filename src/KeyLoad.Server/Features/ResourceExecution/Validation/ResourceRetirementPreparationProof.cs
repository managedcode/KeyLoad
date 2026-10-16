using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparationProof
{
    private const int MinimumOrdinal = 0;
    private const long NoAppliedPosition = 0;

    internal static void RequireControl(ResourceRetirementPreparationControlV1 control,
        ResourceRetirementRequestV1 request, RegisteredPhysicalOwnerV1 source)
    {
        PhysicalOwnerDirectoryValidation.Validate(control.Directory);
        if (control.Request != request || control.Lifetime.TenantId != request.TenantId
            || control.Lifetime.DatabaseId != request.DatabaseId || control.Lifetime.ResourceName != request.ResourceName
            || control.Lifetime.Incarnation != request.ExpectedIncarnation || control.SourceAcceptedMoves.IsDefault
            || control.Catalog.DefaultShard is not { } defaultOwner
            || !PhysicalOwnerEntryValidation.SameOwner(defaultOwner, source.Owner)
            || !PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, source.Owner))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireReceipt(control.PrepareReceipt, control, source.Owner, ResourceRetirementPhase.PrepareRetirement,
            ResourceRetirementIdentity.PreparationCommandId(request));
    }

    internal static void RequireReservation(ResourceRetirementOwnerReservationV1 reservation,
        ResourceRetirementPreparationControlV1 control, RegisteredPhysicalOwnerV1 receiver)
    {
        if (!ResourceRetirementReceiptValidation.Same(reservation.Control, control)
            || !ResourceRetirementReceiptValidation.Same(reservation.Receiver, receiver)
            || reservation.LocalAcceptedMoves.IsDefault || reservation.Attempt is not { } attempt
            || attempt.AttemptId == Guid.Empty || attempt.Ordinal <= MinimumOrdinal || string.IsNullOrEmpty(reservation.OriginalFingerprint)
            || attempt.Stage != ResourceRetirementAttemptStage.Prepared
            || attempt.Error is not null || attempt.SafeDetail is not null
            || attempt.TerminalReceipt is not null || attempt.OriginalFingerprint is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var commandId = ResourceRetirementIdentity.AttemptCommandId(control.Request,
            ResourceRetirementPhase.ReserveOwner, attempt.AttemptId, receiver.Owner.PhysicalShardId);
        if (attempt.OriginalCommandId != commandId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireReceipt(attempt.PrepareReceipt, control, control.Catalog.DefaultShard,
            ResourceRetirementPhase.PrepareAttempt, ResourceRetirementIdentity.AttemptCommandId(control.Request,
                ResourceRetirementPhase.PrepareAttempt, attempt.AttemptId, receiver.Owner.PhysicalShardId));
        RequireReceipt(reservation.Receipt, control, receiver.Owner, ResourceRetirementPhase.ReserveOwner, commandId);
    }

    internal static void RequireReceipt(ResourceRetirementPhaseReceiptV1 receipt,
        ResourceRetirementPreparationControlV1 control, PhysicalShardRecord receiver,
        ResourceRetirementPhase phase, Guid commandId)
    {
        if (receipt.Version != ResourceRetirementNativeProtocol.Version || receipt.RetirementId != control.Request.RetirementId
            || receipt.Phase != phase || receipt.CommandId != commandId || receipt.Lifetime != control.Lifetime
            || receipt.IntentDigest != control.IntentDigest || string.IsNullOrEmpty(receipt.IntentDigest)
            || receipt.AppliedPosition <= NoAppliedPosition || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, receiver))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
