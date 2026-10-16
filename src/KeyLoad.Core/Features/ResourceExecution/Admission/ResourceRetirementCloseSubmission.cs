using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireResourceRetirementCloseSubmission(IKeyValueView view, ReplicatedOperation operation,
        DateTimeOffset admittedAt)
    {
        if (operation.Kind != OperationKind.ResourceRetirementPhase || operation.NativePayload.IsEmpty)
        {
            return;
        }

        var phase = Payload<ResourceRetirementPhaseCommandV1>(operation);
        if (phase.Phase != ResourceRetirementPhase.PreparedOperation)
        {
            return;
        }

        if (ResourceEffectAdmissionValidation.Read(operation, Limits).Error is not null)
        {
            return;
        }

        var body = NativeSerialization.Deserialize<ResourceRetirementPreparedOperationBodyV1>(phase.Body.Span);
        if (body is null || body.Action != ResourceRetirementPreparedOperationAction.Close)
        {
            return;
        }

        ResourceRetirementAttemptContextValidation.Require(body.Context, Limits);
        ResourceRetirementAttemptValidation.Witness(body.Attempt, body.Context, Limits);
        _ = ReadResourceRetirementCloseEligibility(view, body, admittedAt);
    }

    private (ResourceRetirementPreparedOperationV1? Previous, StoredOutcome? Outcome)
        ReadResourceRetirementCloseEligibility(IKeyValueView view, ResourceRetirementPreparedOperationBodyV1 body,
            DateTimeOffset evaluatedAt)
    {
        if (body.Original is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var previous = ResourceRetirementPreparedOperationStorage.Read(view, body.Context, body.Attempt, Limits);
        if (previous?.Stage == ResourceRetirementPreparedOperationStage.Closed)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        StoredOutcome? outcome = null;
        if (previous is not null)
        {
            RequirePreparedResourceRetirementOriginal(previous);
            outcome = ResourceRetirementPreparedOperationStorage.ReadOriginalOutcome(view, previous, Limits);
            if (outcome is not null)
            {
                RequirePreparedResourceRetirementFailedOutcome(outcome, previous);
            }
        }
        if (outcome is null && body.Context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
            && previous?.Original is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }

        if (outcome is null && evaluatedAt < body.Attempt.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }
        return (previous, outcome);
    }
}
