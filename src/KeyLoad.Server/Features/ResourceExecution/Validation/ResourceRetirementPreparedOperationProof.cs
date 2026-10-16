using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementPreparedOperationProof(IOptions<DatabaseLimits> limits)
{
    internal void RequirePersisted(ResourceRetirementPreparedOperationV1 value,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt)
    {
        ResourceRetirementPreparedOperationValidation.Require(value, context, attempt, limits.Value);
        if (value.Stage != ResourceRetirementPreparedOperationStage.Persisted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireOriginalBody(value);
    }

    internal void RequireClosed(ResourceRetirementPreparedOperationV1 value,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 previous)
        => ResourceRetirementPreparedOperationStorage.RequireClosed(value, context, previous, limits.Value);

    private void RequireOriginalBody(ResourceRetirementPreparedOperationV1 value)
    {
        var request = ResourceRetirementAttemptContextValidation.Request(value.Context);
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(value.Original!);
        if (phase.Version != ResourceRetirementNativeProtocol.Version || phase.RetirementId != request.RetirementId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (value.Context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span);
            if (phase.Phase != ResourceRetirementPhase.CleanupPage || body is null
                || !ResourceRetirementReceiptValidation.Same(body.Attempt, value.Attempt)
                || !ResourceRetirementReceiptValidation.Same(body.SealedState.Control, value.Context.Retirement)
                || !ResourceRetirementReceiptValidation.Same(body.Receiver, value.Context.Receiver)
                || body.Partition != value.Context.Page!.Partition
                || (body.Previous is null ? ResourceRetirementNativeProtocol.Version
                    : checked(body.Previous.Ordinal + ResourceRetirementNativeProtocol.Version)) != value.Context.Page.Ordinal
                || ResourceRetirementCleanupPageValidation.Require(body, limits.Value) != value.Original!.Id)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            return;
        }
        if (value.Context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span);
            if (phase.Phase != ResourceRetirementPhase.SealBarrier
                || !ResourceRetirementReceiptValidation.Same(body.Control, value.Context.Retirement)
                || !ResourceRetirementReceiptValidation.Same(body.Attempt, value.Attempt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            ResourceRetirementInventoryValidation.Require(body.Control, value.Attempt, body.Inventory);
            return;
        }
        var reservation = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
        if (phase.Phase != ResourceRetirementPhase.ReserveOwner
            || !ResourceRetirementReceiptValidation.Same(reservation.Context, value.Context)
            || !ResourceRetirementReceiptValidation.Same(reservation.Attempt, value.Attempt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
