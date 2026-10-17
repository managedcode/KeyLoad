using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Requires the real phase-five source outcome, rather than treating a pointer as admission.</summary>
internal static class ResourceRetirementAttemptEvidence
{
    internal static void Require(IKeyValueView view, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            ResourceRetirementCleanupAttemptEvidence.Require(view, context, attempt, limits, observer);
            return;
        }
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(attempt.PrincipalId, attempt.PrepareReceipt.CommandId), limits, observer);
        if (outcome is null || outcome.Incarnation != ResourceRetirementAttemptContextValidation.SourceOwner(context).Incarnation
            || outcome.PolicyEpoch != attempt.PolicyEpoch
            || outcome.ScopeKind != KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind.Global
            || outcome.Partition is not null || outcome.Result is null || outcome.Result.Error is not null
            || outcome.Result.Json is not null || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, attempt.PrepareReceipt)
            || result.Fence is not null || result.PreparedOperation is not null || result.OwnerReservation is not null || ResourceRetirementWorkValidation.HasCompletionArm(result))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var prepared = ResourceRetirementAttemptValidation.Prepared(attempt);
        if (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            if (result.PreparationState is not null || result.State is null
                || result.State.Stage != ResourceRetirementStage.Retiring
                || !ResourceRetirementValidation.Same(result.State.Control, context.Retirement)
                || !ResourceRetirementValidation.Same(result.State.SealAttempt, prepared)
                || result.State.FinalInventory is not null || result.State.SealedReceipt is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return;
        }
        if (result.State is not null || result.PreparationState is null
            || !ResourceRetirementValidation.Same(result.PreparationState.Control, context.Preparation)
            || result.PreparationState.BegunControl is not null || result.PreparationState.OwnerAttempts.IsDefault
            || !result.PreparationState.OwnerAttempts.Any(value => ResourceRetirementValidation.Same(value, prepared)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
