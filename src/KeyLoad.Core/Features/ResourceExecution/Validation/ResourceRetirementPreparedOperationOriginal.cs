using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequirePreparedResourceRetirementOriginal(ResourceRetirementPreparedOperationV1 prepared)
    {
        ResourceRetirementPreparedOperationValidation.Require(prepared, prepared.Context, prepared.Attempt, Limits);
        if (prepared.Original is null)
        {
            return;
        }
        var original = VerifyOperationAuthority(prepared.Original);
        var context = prepared.Context;
        var attempt = prepared.Attempt;
        var phase = Payload<ResourceRetirementPhaseCommandV1>(original);
        var expectedPhase = ResourceRetirementCleanupAttemptScope.OriginalPhase(context);
        if (original.Kind != OperationKind.ResourceRetirementPhase || phase.Version != ResourceRetirementProtocol.Version
            || phase.Phase != expectedPhase || phase.RetirementId != ResourceRetirementAttemptContextValidation.Request(context).RetirementId
            || original.Id != attempt.OriginalCommandId || original.EvaluatedAt >= attempt.ExpiresAt
            || !ResourceRetirementPreparedOperationStorage.Exact(original, prepared.Original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        if (expectedPhase == ResourceRetirementPhase.SealBarrier)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span);
            if (original.PrincipalId != attempt.PrincipalId || body is null
                || !ResourceRetirementValidation.Same(body.Control, context.Retirement)
                || !ResourceRetirementValidation.Same(body.Attempt, attempt)
                || !ResourceRetirementValidation.Same(body.Inventory?.Control?.RetirementAttempt, attempt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementInventoryValidation.Require(body.Inventory!, body.Control, Limits);
        }
        else if (expectedPhase == ResourceRetirementPhase.CleanupPage)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span);
            var expectedId = ResourceRetirementCleanupPageValidation.Require(body, Limits);
            if (expectedId != original.Id || !ResourceRetirementValidation.Same(body.Attempt, attempt)
                || !ResourceRetirementValidation.Same(ResourceRetirementCleanupAttemptScope.Context(body), context))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        else
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
            if (body is null || !ResourceRetirementValidation.Same(body.Context, context)
                || !ResourceRetirementValidation.Same(body.Attempt, attempt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
    }

    private ResourceRetirementPreparedOperationV1 RequireExactPreparedResourceRetirementOperation(
        IKeyValueView view, ReplicatedOperation operation, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt)
    {
        var prepared = ResourceRetirementPreparedOperationStorage.Read(view, context, attempt, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        if (prepared.Stage != ResourceRetirementPreparedOperationStage.Persisted || prepared.Original is null
            || !ResourceRetirementPreparedOperationStorage.Exact(prepared.Original, operation))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        RequirePreparedResourceRetirementOriginal(prepared);
        return prepared;
    }

    private ResourceRetirementPreparedOperationV1 RequireExactPreparedResourceRetirementSeal(
        IKeyValueView view, ReplicatedOperation operation, ResourceRetirementSealBodyV1 body)
        => RequireExactPreparedResourceRetirementOperation(view, operation,
            new(ResourceRetirementAttemptPurpose.SealBarrier, body.Control, null, null),
            body.Attempt ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed));

    private ResourceRetirementPreparedOperationV1 RequirePreparedResourceRetirementReservation(
        IKeyValueView view, ReplicatedOperation operation, ResourceRetirementReserveOwnerBodyV1 body)
    {
        if (body.Context.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        var prepared = RequireExactPreparedResourceRetirementOperation(view, operation, body.Context, body.Attempt);
        RequirePreparedResourceRetirementCommittedDeadline(view, body.Attempt);
        return prepared;
    }
}
