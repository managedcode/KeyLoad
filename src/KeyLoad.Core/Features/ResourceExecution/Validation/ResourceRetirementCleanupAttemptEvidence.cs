using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupAttemptEvidence
{
    internal static void Require(IKeyValueView view, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits, StorageReadObserver? observer)
    {
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(attempt.PrincipalId, attempt.PrepareReceipt.CommandId), limits, observer);
        if (outcome is null || outcome.Incarnation != context.Retirement!.Catalog.DefaultShard.Incarnation
            || outcome.PolicyEpoch != attempt.PolicyEpoch || outcome.ScopeKind != CommandOutcomeScopeKind.Global
            || outcome.Partition is not null || outcome.Result is null || outcome.Result.Error is not null
            || outcome.Result.Json is not null || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, attempt.PrepareReceipt)
            || !ResourceRetirementValidation.Same(result.CleanupAttempt, ResourceRetirementAttemptValidation.Prepared(attempt))
            || result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.PreparationState is not null || result.OwnerReservation is not null
            || result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null
            || result.Finalization is not null || result.OwnerRelease is not null || result.DropParent is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementValidation.Bound(outcome, limits);
    }
}
