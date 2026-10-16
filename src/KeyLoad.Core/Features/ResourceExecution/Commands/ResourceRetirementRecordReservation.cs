using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult RecordResourceRetirementReservation(IAtomicTransaction transaction, ReplicatedOperation operation,
        ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordReservationBodyV1>(phase.Body.Span);
        if (body is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var state = RequirePreparationSource(transaction, body.Control);
        ResourceRetirementPreparationValidation.Reservation(body.Reservation, Limits);
        var context = ResourceRetirementAttemptValidation.Owner(body.Control, body.Reservation.Receiver);
        if (!ResourceRetirementValidation.Same(body.Reservation.Control, body.Control) || state.BegunControl is not null
            || phase.RetirementId != body.Control.Request.RetirementId
            || operation.Id != ResourceRetirementIdentity.AttemptCommandId(body.Control.Request, phase.Phase,
                body.Reservation.Attempt!.AttemptId, body.Reservation.Receiver.Owner.PhysicalShardId))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var existing = state.Reservations.SingleOrDefault(reservation =>
            PhysicalOwnerEntryValidation.SameOwner(reservation.Receiver.Owner, body.Reservation.Receiver.Owner));
        if (existing is not null && !ResourceRetirementValidation.Same(existing, body.Reservation))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        if (existing is null)
        {
            var attempt = ResourceRetirementAttemptStorage.RequirePrepared(transaction, context, body.Reservation.Attempt!, Limits);
            var completed = attempt with
            {
                Stage = ResourceRetirementAttemptStage.Completed,
                OriginalFingerprint = body.Reservation.OriginalFingerprint,
                TerminalReceipt = body.Reservation.Receipt
            };
            ResourceRetirementAttemptStorage.Write(transaction, context, completed, Limits);
            state = state with
            {
                Reservations = state.Reservations.Add(body.Reservation),
                OwnerAttempts = state.OwnerAttempts.Replace(attempt, completed)
            };
        }
        var result = new ResourceRetirementPhaseResultV1(
            PreparationReceipt(transaction, body.Control, operation, phase.Phase, position), null, null)
        {
            PreparationState = state
        };
        ResourceRetirementValidation.Bound(result, Limits);
        ResourceRetirementPreparationStorage.WriteState(transaction, state, Limits);
        return Result(result);
    }
}
