using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementStateV1 ReadResourceRetirementOriginalWorkSeal(string principalId,
        ResourceRetirementControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            var state = RequireRetirementSource(view, control);
            return ReadOriginalResourceRetirementWorkBarrier(view, state);
        });
        work.CheckResult(result);
        return result;
    }

    private ResourceRetirementStateV1 ReadOriginalResourceRetirementWorkBarrier(IKeyValueView view,
        ResourceRetirementStateV1 state)
    {
        var barrier = state.WorkBarrierReceipt;
        if (barrier?.Original is not { } original || barrier.PolicyEpoch is not { } epoch)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        original = VerifyOperationAuthority(original);
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        var body = PayloadWorkSeal(phase);
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(original.PrincipalId, barrier.Receipt.CommandId), Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (original.Id != barrier.Receipt.CommandId || original.Kind != OperationKind.ResourceRetirementPhase
            || phase.Phase != ResourceRetirementPhase.SealAllWork
            || phase.RetirementId != state.Control.Request.RetirementId
            || !ResourceRetirementValidation.Same(body.Control, state.Control)
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(original) || outcome.PolicyEpoch != epoch
            || outcome.Incarnation != state.Control.Catalog.DefaultShard.Incarnation
            || outcome.Partition is not null || outcome.ScopeKind != CommandOutcomeScopeKind.Global
            || outcome.Result is not { Error: null, Json: null, NativeValue: ResourceRetirementPhaseResultV1 result }
            || result.State is not { } saved || result.WorkOwnerReceipt is not null || result.Fence is not null
            || result.PreparedOperation is not null || result.PreparationState is not null || result.OwnerReservation is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null || result.Finalization is not null
            || result.OwnerRelease is not null || result.DropParent is not null || result.CleanupAttempt is not null
            || !ResourceRetirementValidation.Same(result.Receipt, barrier.Receipt)
            || !ResourceRetirementValidation.Same(result.WorkBarrierReceipt, barrier)
            || !ResourceRetirementValidation.Same(saved.WorkBarrierReceipt, barrier)
            || !ResourceRetirementValidation.Same(saved.Control, state.Control)
            || !saved.OwnerCleanupReceipts.IsEmpty || saved.Finalization is not null
            || !saved.OwnerReleaseReceipts.IsEmpty || saved.CompletionReceipt is not null
            || saved.CompletionOperation is not null || saved.CompletionPolicyEpoch is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementWorkValidation.RequireBarrier(saved, Limits);
        return saved;
    }

    private static ResourceRetirementSealAllWorkBodyV1 PayloadWorkSeal(ResourceRetirementPhaseCommandV1 phase)
    {
        if (phase is null || phase.Phase != ResourceRetirementPhase.SealAllWork || phase.Body.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        return NativeSerialization.Deserialize<ResourceRetirementSealAllWorkBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
    }
}
