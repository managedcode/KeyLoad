using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal (ResourceRetirementOwnerFenceV1 Fence, ResourceRetirementWorkOwnerReceiptV1? Owner)
        CaptureResourceRetirementWorkObservation(string principalId, ResourceRetirementControlV1 control,
            ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementValidation.Control(control, Limits);
            var member = ResourceRetirementStateValidation.Member(control, ReadInventoryPhysicalCatalog(view).DefaultShard);
            RequireRetirementReceiver(view, control, member);
            var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, Limits)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            RequireSameRetirement(fence.Control, control);
            var owner = ResourceRetirementWorkStorage.ReadOwner(view, control, Limits);
            if (owner is not null && !ResourceRetirementValidation.Same(owner.Fence, fence))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return (Fence: fence, Owner: owner);
        });
        work.CheckResult(result.Fence);
        if (result.Owner is not null)
        {
            work.CheckResult(result.Owner);
        }
        return result;
    }
    internal ResourceRetirementStateV1 ReadResourceRetirementOriginalSeal(string principalId,
        ResourceRetirementControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var original = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            var current = RequireRetirementSource(view, control);
            var attempt = current.SealAttempt;
            if (current.Stage != ResourceRetirementStage.BarrierSealed || current.SealedReceipt is null
                || attempt is not { Stage: ResourceRetirementAttemptStage.Completed })
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            }
            var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
                KeySpace.GlobalOutcome(attempt.PrincipalId, current.SealedReceipt.CommandId), Limits)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            if (outcome.Fingerprint != attempt.OriginalFingerprint || outcome.PolicyEpoch != attempt.PolicyEpoch
                || outcome.Incarnation != control.Catalog.DefaultShard.Incarnation || outcome.Partition is not null
                || outcome.ScopeKind != KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind.Global
                || outcome.Result is not { Error: null, Json: null, NativeValue: ResourceRetirementPhaseResultV1 result }
                || result.State is not { Stage: ResourceRetirementStage.BarrierSealed } sealedState
                || ResourceRetirementWorkValidation.HasCompletionArm(result)
                || result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
                || result.OwnerReservation is not null || !sealedState.WorkOwnerReceipts.IsEmpty || sealedState.WorkBarrierReceipt is not null
                || !ResourceRetirementValidation.Same(result.Receipt, current.SealedReceipt)
                || !ResourceRetirementValidation.Same(sealedState.SealedReceipt, current.SealedReceipt)
                || !ResourceRetirementValidation.Same(sealedState.Control, control)
                || !ResourceRetirementValidation.Same(sealedState.FinalInventory, current.FinalInventory))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementStateValidation.State(sealedState, Limits);
            return sealedState;
        });
        work.CheckResult(original);
        return original;
    }

}
