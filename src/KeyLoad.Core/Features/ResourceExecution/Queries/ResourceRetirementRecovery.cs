using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementStateV1 ReadResourceRetirementState(string principalId,
        ResourceRetirementRequestV1 request, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementValidation.Request(request, Limits);
            var state = ResourceRetirementStorage.ReadControl(view, request, Limits)
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            if (!ResourceRetirementValidation.Same(state.Control.Request, request))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
            if (state.Stage is ResourceRetirementStage.CatalogFinalized or ResourceRetirementStage.Completed)
            {
                return RequireResourceRetirementFinalizedSource(view, state.Control);
            }

            RequireRetirementBeginOutcome(view, state.Control);
            return state;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }

    internal ResourceRetirementOwnerFenceV1 ReadResourceRetirementOwnerFence(string principalId,
        ResourceRetirementControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementValidation.Control(control, Limits);
            var receiver = ResourceRetirementStateValidation.Member(control, ReadInventoryPhysicalCatalog(view).DefaultShard);
            RequireRetirementReceiver(view, control, receiver);
            var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, Limits)
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            RequireSameRetirement(fence.Control, control);
            CloseCommittedResourceRetirementFence(view, fence);
            return fence;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }

    private void RequireRetirementBeginOutcome(IKeyValueView view, ResourceRetirementControlV1 control)
    {
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(control.PrincipalId, control.Request.RetirementId), Limits)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        if (outcome.Fingerprint != control.IntentDigest || outcome.PolicyEpoch != control.PolicyEpoch
            || outcome.Incarnation != control.Catalog.DefaultShard.Incarnation || outcome.Result is null
            || outcome.ScopeKind != KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind.Global
            || outcome.Partition is not null || outcome.Result.Json is not null
            || outcome.Result.Error is not null || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, control.BeginReceipt)
            || result.State is null || !ResourceRetirementValidation.Same(result.State.Control, control)
            || result.State.Stage != ResourceRetirementStage.Retiring || result.State.OwnerReceipts.IsDefault || !result.State.OwnerReceipts.IsEmpty
            || result.State.FinalInventory is not null || result.State.SealedReceipt is not null
            || result.State.SealAttempt is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.PreparationState is not null || result.OwnerReservation is not null || ResourceRetirementWorkValidation.HasCompletionArm(result))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
