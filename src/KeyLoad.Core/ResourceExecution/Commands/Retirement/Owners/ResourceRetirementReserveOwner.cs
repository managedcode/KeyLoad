using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ReserveResourceRetirementOwner(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
        if (body is null || body.Context?.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementAttemptValidation.Witness(body.Attempt, body.Context, Limits);
        var control = body.Context.Preparation!;
        var receiver = body.Context.Receiver!;
        RequirePreparationReceiver(transaction, control, receiver);
        if (phase.RetirementId != control.Request.RetirementId || operation.Id != body.Attempt.OriginalCommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        _ = RequirePreparedResourceRetirementReservation(transaction, operation, body);
        if (body.Attempt.ExpiresAt <= operation.EvaluatedAt)
        {
            return new(null, ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
        }
        if (ResourceRetirementPreparationStorage.ReadReservation(transaction, control.Lifetime, Limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        ResourceRetirementStorage.RequireOpen(transaction, control.Lifetime, Limits);
        var receipt = PreparationReceipt(transaction, control, operation, phase.Phase, position);
        var reservation = new ResourceRetirementOwnerReservationV1(control, receiver, [], receipt)
        {
            Attempt = body.Attempt,
            OriginalFingerprint = NativeOperationFingerprint.Compute(operation)
        };
        ResourceRetirementPreparationValidation.Reservation(reservation, Limits);
        var result = new ResourceRetirementPhaseResultV1(receipt, null, null) { OwnerReservation = reservation };
        ResourceRetirementValidation.Bound(result, Limits);
        ResourceRetirementPreparationStorage.CreateOwner(transaction, reservation, Limits);
        return Result(result);
    }
}
