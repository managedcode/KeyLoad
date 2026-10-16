using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementDurableReplyValidation
{
    internal static void Require(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(result, limits);
        if (result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null || result.Finalization is not null
            || result.OwnerRelease is not null || result.DropParent is not null
            || result.CleanupAttempt is not null && (phase.Phase != ResourceRetirementPhase.PrepareAttempt
                || NativeSerialization.Deserialize<ResourceRetirementPrepareAttemptBodyV1>(phase.Body.Span).Context.Purpose
                    != ResourceRetirementAttemptPurpose.CleanupPage))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        var valid = phase.Phase switch
        {
            ResourceRetirementPhase.PrepareAttempt => PrepareMatches(phase, result, limits),
            ResourceRetirementPhase.PreparedOperation => PreparedMatches(phase, result, limits),
            _ => false
        };
        if (!valid)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
    }

    private static bool PrepareMatches(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPrepareAttemptBodyV1>(phase.Body.Span);
        if (body is null || result.PreparedOperation is not null || result.OwnerReservation is not null || result.Fence is not null)
        {
            return false;
        }
        var attempt = body.Context.Purpose switch
        {
            ResourceRetirementAttemptPurpose.CleanupPage when result.State is null && result.PreparationState is null
                => result.CleanupAttempt,
            ResourceRetirementAttemptPurpose.SealBarrier when result.PreparationState is null
                && result.State is { } state && ResourceRetirementValidation.Same(state.Control, body.Context.Retirement)
                => state.SealAttempt,
            ResourceRetirementAttemptPurpose.OwnerReservation when result.State is null
                && result.PreparationState is { } preparation && ResourceRetirementValidation.Same(preparation.Control, body.Context.Preparation)
                => preparation.OwnerAttempts.FirstOrDefault(attempt => attempt.AttemptId == body.AttemptId),
            _ => null
        };
        if (attempt is not null)
        {
            KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation.Require(attempt, body.Context, limits);
        }
        return attempt is not null && attempt.AttemptId == body.AttemptId && attempt.ExpiresAt == body.ExpiresAt
            && ResourceRetirementValidation.Same(result.Receipt, attempt.PrepareReceipt);
    }

    private static bool PreparedMatches(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPreparedOperationBodyV1>(phase.Body.Span);
        if (body is null || result.PreparedOperation is not { } prepared || result.State is not null || result.Fence is not null
            || result.PreparationState is not null || result.OwnerReservation is not null
            || !ResourceRetirementPreparedReplyValidation.Matches(body.Context, body.Attempt, prepared, limits)
            || !ResourceRetirementValidation.Same(result.Receipt, prepared.Receipt))
        {
            return false;
        }
        return body.Action switch
        {
            ResourceRetirementPreparedOperationAction.Persist => prepared.Stage == ResourceRetirementPreparedOperationStage.Persisted
                && ResourceRetirementValidation.Same(prepared.Original, body.Original)
                && ResourceRetirementValidation.Same(prepared.PersistReceipt, prepared.Receipt),
            ResourceRetirementPreparedOperationAction.Close => prepared.Stage == ResourceRetirementPreparedOperationStage.Closed,
            _ => false
        };
    }
}
