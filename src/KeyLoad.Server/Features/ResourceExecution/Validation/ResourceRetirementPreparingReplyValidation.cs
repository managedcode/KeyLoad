using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparingReplyValidation
{
    internal static void Require(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result,
        DatabaseLimits limits)
    {
        if (result is null || result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.Receipt is not { } receipt || receipt.Version != ResourceRetirementNativeProtocol.Version
            || receipt.RetirementId != phase.RetirementId || receipt.Phase != phase.Phase || receipt.AppliedPosition <= 0)
        {
            throw Invalid();
        }
        var control = result.PreparationState?.Control ?? result.OwnerReservation?.Control ?? throw Invalid();
        ResourceRetirementPreparationValidation.Control(control, limits);
        ResourceRetirementValidation.Bound(result, limits);
        if (receipt.IntentDigest != control.IntentDigest || !ResourceRetirementValidation.Same(receipt.Lifetime, control.Lifetime))
        {
            throw Invalid();
        }
        var valid = phase.Phase switch
        {
            ResourceRetirementPhase.PrepareRetirement => result.PreparationState is { } preparation
                && result.OwnerReservation is null && PrepareMatches(phase, preparation)
                && ResourceRetirementValidation.Same(receipt, control.PrepareReceipt),
            ResourceRetirementPhase.ReserveOwner => result.OwnerReservation is { } reservation
                && result.PreparationState is null && ReserveMatches(phase, reservation, limits)
                && ResourceRetirementValidation.Same(receipt, reservation.Receipt),
            ResourceRetirementPhase.RecordReservation => result.PreparationState is { } preparation
                && result.OwnerReservation is null && RecordMatches(phase, preparation),
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    private static bool PrepareMatches(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPreparationStateV1 state)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPrepareBodyV1>(phase.Body.Span);
        return body is not null && ResourceRetirementValidation.Same(body.Request, state.Control.Request);
    }

    private static bool ReserveMatches(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementOwnerReservationV1 reservation,
        DatabaseLimits limits)
    {
        ResourceRetirementPreparationValidation.Reservation(reservation, limits);
        var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
        return body is not null && ResourceRetirementValidation.Same(body.Context.Preparation, reservation.Control)
            && ResourceRetirementValidation.Same(body.Context.Receiver, reservation.Receiver)
            && reservation.Attempt is { } attempt && ResourceRetirementNativeAttemptValidation.SameIdentity(attempt, body.Attempt)
            && attempt.OriginalCommandId == reservation.Receipt.CommandId
            && attempt.Stage == ResourceRetirementAttemptStage.Prepared
            && ResourceRetirementValidation.Same(attempt, body.Attempt)
            && ResourceRetirementValidation.Digest(reservation.OriginalFingerprint!);
    }

    private static bool RecordMatches(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPreparationStateV1 state)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordReservationBodyV1>(phase.Body.Span);
        return body is not null && ResourceRetirementValidation.Same(body.Control, state.Control)
            && !state.Reservations.IsDefault
            && state.Reservations.Any(reservation => ResourceRetirementValidation.Same(reservation, body.Reservation));
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
}
