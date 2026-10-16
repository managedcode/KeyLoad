using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparedSealValidation
{
    internal static ResourceRetirementSealBodyV1 Require(ResourceRetirementPreparedOperationV1 prepared,
        ResourceRetirementControlV1 control, ResourceRetirementAttemptV1 attempt, PhysicalShardRecord source)
    {
        ResourceRetirementAttemptValidation.Require(attempt, control, source);
        if (prepared.Context.Purpose != ResourceRetirementAttemptPurpose.SealBarrier
            || !ResourceRetirementReceiptValidation.Same(prepared.Context.Retirement, control)
            || prepared.Attempt.Stage != ResourceRetirementAttemptStage.Prepared
            || !ResourceRetirementAttemptValidation.SameAdmission(prepared.Attempt, attempt)
            || prepared.Original is not { } original || original.Id != attempt.OriginalCommandId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span);
        if (phase.Phase != ResourceRetirementPhase.SealBarrier || phase.RetirementId != control.Request.RetirementId
            || !ResourceRetirementReceiptValidation.Same(body.Control, control)
            || !ResourceRetirementReceiptValidation.Same(body.Attempt, prepared.Attempt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        ResourceRetirementInventoryValidation.Require(control, prepared.Attempt, body.Inventory);
        return body;
    }
}
