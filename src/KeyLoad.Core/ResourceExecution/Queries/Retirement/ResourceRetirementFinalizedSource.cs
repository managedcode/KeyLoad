using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    // Uses the retired lifetime key and retained authority. A reused live resource name is never consulted.
    private ResourceRetirementStateV1 RequireResourceRetirementFinalizedSource(IKeyValueView view,
        ResourceRetirementControlV1 control)
    {
        ResourceRetirementValidation.Control(control, Limits);
        var state = ResourceRetirementStorage.ReadControl(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(state.Control, control);
        if (state.Stage is not (ResourceRetirementStage.CatalogFinalized or ResourceRetirementStage.Completed)
            || state.Finalization is not { } completed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }

        var owner = ReadInventoryPhysicalCatalog(view).DefaultShard;
        if (!PhysicalOwnerEntryValidation.SameOwner(owner, control.Catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }

        var history = ResourceRetirementFinalizationStorage.ReadHistory(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (!ResourceRetirementValidation.Same(history, completed))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        RequireRetirementBeginOutcome(view, control);
        RequireResourceRetirementFinalizationOutcome(view, completed);
        if (state.Stage == ResourceRetirementStage.Completed)
        {
            RequireResourceRetirementTerminalOutcome(view, state);
        }

        return state;
    }

    private void RequireResourceRetirementTerminalOutcome(IKeyValueView view, ResourceRetirementStateV1 state)
    {
        ResourceRetirementCompletionValidation.RequireCompleted(state, Limits);
        var original = VerifyOperationAuthority(state.CompletionOperation!);
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(original.PrincipalId, original.Id), Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (outcome.Incarnation != state.Control.Catalog.DefaultShard.Incarnation
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(original)
            || outcome.PolicyEpoch != state.CompletionPolicyEpoch || outcome.ScopeKind != CommandOutcomeScopeKind.Global
            || outcome.Partition is not null || outcome.Result is null || outcome.Result.Error is not null
            || outcome.Result.Json is not null || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, state.CompletionReceipt)
            || !ResourceRetirementValidation.Same(result.State, state)
            || result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null || result.Finalization is not null
            || result.OwnerRelease is not null || result.DropParent is not null || result.CleanupAttempt is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private void RequireResourceRetirementFinalizationOutcome(IKeyValueView view,
        ResourceRetirementFinalizationV1 completed)
    {
        ResourceRetirementFinalizationValidation.Require(completed, Limits);
        var original = VerifyOperationAuthority(completed.Original!);
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(original.PrincipalId, original.Id), Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (outcome.Incarnation != completed.Control.Catalog.DefaultShard.Incarnation
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(original)
            || outcome.PolicyEpoch != completed.PolicyEpoch || outcome.ScopeKind != CommandOutcomeScopeKind.Global
            || outcome.Partition is not null || outcome.Result is null
            || outcome.Result.Json is not null || outcome.Result.Error is not null
            || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, completed.Receipt)
            || !ResourceRetirementValidation.Same(result.Finalization, completed)
            || result.State is not { } originalState || originalState.Stage != ResourceRetirementStage.CatalogFinalized
            || !ResourceRetirementValidation.Same(originalState.Control, completed.Control)
            || !ResourceRetirementValidation.Same(originalState.Finalization, completed)
            || originalState.OwnerReleaseReceipts.IsDefault || !originalState.OwnerReleaseReceipts.IsEmpty || originalState.CompletionReceipt is not null
            || originalState.CompletionOperation is not null || originalState.CompletionPolicyEpoch is not null
            || result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null
            || result.OwnerRelease is not null || result.DropParent is not null || result.CleanupAttempt is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementStateValidation.State(originalState, Limits);
    }
}
