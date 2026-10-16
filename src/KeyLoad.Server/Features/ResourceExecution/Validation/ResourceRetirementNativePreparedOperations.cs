using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementNativePreparedOperations
{
    internal static ReplicatedOperation Issue(DatabaseEngine database, PrincipalRecord principal, Guid commandId,
        ResourceRetirementNativePhaseRequestV1 request, TimeProvider clock, ReadExecutionBudget work)
    {
        if (request.OriginalOperation is not null || request.Phase.Phase is not
            (ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.ReserveOwner or ResourceRetirementPhase.CleanupPage))
        {
            throw Invalid();
        }
        RequireBody(database, principal, commandId, request);
        var operation = database.CreateResourceRetirementOperation(commandId, principal.Id, clock.GetUtcNow(), request.Phase, work);
        ResourceRetirementValidation.Bound(operation, database.Limits);
        if (NativeSerialization.Measure(operation) > request.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        return operation;
    }

    internal static ReplicatedOperation ForSubmission(DatabaseEngine database, PrincipalRecord principal, Guid commandId,
        ResourceRetirementNativePhaseRequestV1 request, TimeProvider clock, ReadExecutionBudget work)
    {
        if (request.Phase.Phase is not (ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.ReserveOwner or ResourceRetirementPhase.CleanupPage))
        {
            if (request.OriginalOperation is not null)
            {
                throw Invalid();
            }
            return database.CreateResourceRetirementOperation(commandId, principal.Id, clock.GetUtcNow(), request.Phase, work);
        }
        if (request.OriginalOperation is not { } original)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireBody(database, principal, commandId, request);
        ResourceRetirementValidation.Bound(original, database.Limits);
        var verified = database.VerifyOperationAuthority(original);
        if (verified.Kind != OperationKind.ResourceRetirementPhase || verified.Id != commandId || verified.PrincipalId != principal.Id
            || !ResourceRetirementValidation.Same(NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(verified), request.Phase))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        // The Core owner certifies the entire stored original, including evaluation time and all native admission bytes.
        _ = database.PreservePreparedResourceRetirementEvaluation(verified, clock.GetUtcNow(), work);
        return verified;
    }

    private static void RequireBody(DatabaseEngine database, PrincipalRecord principal, Guid commandId,
        ResourceRetirementNativePhaseRequestV1 request)
    {
        if (ResourceRetirementPhaseBodyValidation.Require(request.Phase, database.Limits) != commandId)
        {
            throw Invalid();
        }
        ResourceRetirementAttemptContextV1 context;
        ResourceRetirementAttemptV1 attempt;
        if (request.Phase.Phase == ResourceRetirementPhase.CleanupPage)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(request.Phase.Body.Span);
            attempt = body?.Attempt ?? throw Invalid();
            context = new(ResourceRetirementAttemptPurpose.CleanupPage, body.SealedState.Control, null, body.Receiver)
            {
                Page = new(body.Partition, body.Previous is null ? ResourceRetirementNativeProtocol.Version
                    : checked(body.Previous.Ordinal + ResourceRetirementNativeProtocol.Version))
            };
        }
        else if (request.Phase.Phase == ResourceRetirementPhase.SealBarrier)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(request.Phase.Body.Span);
            if (body?.Control is null || body.Attempt is null || body.Inventory?.Control is null
                || !ResourceRetirementValidation.Same(body.Attempt, body.Inventory.Control.RetirementAttempt))
            {
                throw Invalid();
            }
            context = new(ResourceRetirementAttemptPurpose.SealBarrier, body.Control, null, null);
            attempt = body.Attempt;
            if (attempt.PrincipalId != principal.Id || attempt.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        else
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(request.Phase.Body.Span);
            context = body?.Context ?? throw Invalid();
            attempt = body.Attempt;
            if (context.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation)
            {
                throw Invalid();
            }
        }
        ResourceRetirementAttemptContextValidation.Require(context, database.Limits);
        KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation.Witness(attempt, context, database.Limits);
        if (attempt.Stage != ResourceRetirementAttemptStage.Prepared || attempt.OriginalCommandId != commandId
            || request.ExpiresAt > attempt.ExpiresAt)
        {
            throw Invalid();
        }
        if (!PhysicalOwnerEntryValidation.SameOwner(ResourceRetirementAttemptContextValidation.ExecutionOwner(context), request.ObservedOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
}
