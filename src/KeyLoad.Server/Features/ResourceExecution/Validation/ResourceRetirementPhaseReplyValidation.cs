using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPhaseReplyValidation
{
    internal static void Require(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        if (ResourceRetirementCompletionPhase.Handles(phase.Phase))
        {
            ResourceRetirementCompletionReply.Require(phase, result, limits);
            return;
        }
        if (phase.Phase is ResourceRetirementPhase.CommitOwnerWorkJoin or ResourceRetirementPhase.RecordOwnerWorkJoin
            or ResourceRetirementPhase.SealAllWork)
        {
            ResourceRetirementWorkReplyValidation.Require(phase, result, limits);
            return;
        }
        if (phase.Phase is ResourceRetirementPhase.PrepareAttempt or ResourceRetirementPhase.PreparedOperation)
        {
            ResourceRetirementDurableReplyValidation.Require(phase, result, limits);
            return;
        }
        if (ResourceRetirementWorkValidation.HasCompletionArm(result))
        {
            throw Invalid();
        }
        if (phase.Phase is ResourceRetirementPhase.PrepareRetirement or ResourceRetirementPhase.ReserveOwner
            or ResourceRetirementPhase.RecordReservation)
        {
            ResourceRetirementPreparingReplyValidation.Require(phase, result, limits);
            return;
        }
        if (result.PreparationState is not null || result.OwnerReservation is not null || result.PreparedOperation is not null)
        {
            throw Invalid();
        }
        var control = result.State?.Control ?? result.Fence?.Control ?? throw Invalid();
        ResourceRetirementValidation.Control(control, limits);
        if (result.Receipt.Version != ResourceRetirementNativeProtocol.Version || result.Receipt.AppliedPosition <= 0
            || result.Receipt.IntentDigest != control.IntentDigest
            || !ResourceRetirementValidation.Same(result.Receipt.Lifetime, control.Lifetime))
        {
            throw Invalid();
        }
        var valid = phase.Phase switch
        {
            ResourceRetirementPhase.Begin => result.State is not null && result.Fence is null
                && ResourceRetirementValidation.Same(result.Receipt, control.BeginReceipt),
            ResourceRetirementPhase.FenceOwner => result.Fence is { } fence && result.State is null
                && ResourceRetirementValidation.Same(result.Receipt, fence.Receipt),
            ResourceRetirementPhase.RecordOwnerReceipt => result.State is not null && result.Fence is null,
            ResourceRetirementPhase.SealBarrier => result.State is { } state && result.Fence is null
                && state.SealedReceipt is not null && ResourceRetirementValidation.Same(result.Receipt, state.SealedReceipt)
                && state.SealAttempt is { Stage: ResourceRetirementAttemptStage.Completed, TerminalReceipt: { } receipt }
                && ResourceRetirementValidation.Same(result.Receipt, receipt),
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
}
