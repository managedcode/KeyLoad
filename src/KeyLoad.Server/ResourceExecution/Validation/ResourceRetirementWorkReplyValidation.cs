using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementWorkReplyValidation
{
    internal static void Require(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result,
        DatabaseLimits limits)
    {
        if (result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || result.CleanupCheckpoint is not null
            || result.OwnerCleanup is not null || result.Finalization is not null
            || result.OwnerRelease is not null || result.DropParent is not null || result.CleanupAttempt is not null)
        {
            throw Invalid();
        }
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.CommitOwnerWorkJoin:
                var body = NativeSerialization.Deserialize<ResourceRetirementCommitOwnerWorkJoinBodyV1>(phase.Body.Span);
                var owner = result.WorkOwnerReceipt ?? throw Invalid();
                ResourceRetirementWorkValidation.Owner(owner, body.SealedState.Control, limits);
                if (result.State is not null || result.WorkBarrierReceipt is not null
                    || !ResourceRetirementValidation.Same(owner.Evidence, body.Evidence)
                    || !ResourceRetirementValidation.Same(result.Receipt, owner.Receipt))
                {
                    throw Invalid();
                }
                break;
            case ResourceRetirementPhase.RecordOwnerWorkJoin:
                var record = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerWorkJoinBodyV1>(phase.Body.Span);
                var state = result.State ?? throw Invalid();
                ResourceRetirementStateValidation.State(state, limits);
                ResourceRetirementReceiptValidation.RequireReceipt(result.Receipt, record.Control,
                    ResourceRetirementPhase.RecordOwnerWorkJoin, record.Control.Catalog.DefaultShard,
                    record.OwnerReceipt.Receiver.Owner.PhysicalShardId);
                if (ResourceRetirementWorkValidation.HasCompletionArm(result)
                    || !ResourceRetirementValidation.Same(state.Control, record.Control)
                    || !state.WorkOwnerReceipts.Any(item => ResourceRetirementValidation.Same(item, record.OwnerReceipt)))
                {
                    throw Invalid();
                }
                break;
            case ResourceRetirementPhase.SealAllWork:
                var seal = NativeSerialization.Deserialize<ResourceRetirementSealAllWorkBodyV1>(phase.Body.Span);
                var barrier = result.WorkBarrierReceipt ?? throw Invalid();
                if (result.State is not { } originalState || result.WorkOwnerReceipt is not null
                    || !ResourceRetirementValidation.Same(originalState.WorkBarrierReceipt, barrier)
                    || !ResourceRetirementValidation.Same(barrier.Control, seal.Control)
                    || !ResourceRetirementValidation.Same(barrier.Receipt, result.Receipt))
                {
                    throw Invalid();
                }
                ResourceRetirementWorkValidation.RequireBarrier(originalState, limits);
                ResourceRetirementValidation.Receipt(barrier.Receipt, seal.Control,
                    ResourceRetirementPhase.SealAllWork, seal.Control.Catalog.DefaultShard, limits);
                break;
            default:
                throw Invalid();
        }
        ResourceRetirementValidation.Bound(result, limits);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
}
