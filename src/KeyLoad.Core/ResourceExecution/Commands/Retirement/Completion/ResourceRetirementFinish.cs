using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 RecordResourceRetirementOwnerRelease(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerReleaseBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        var state = RequireResourceRetirementFinalizedSource(view, body.Control);
        RequireRetirementPhase(operation, phase, body.Control, body.OwnerRelease.Receiver.Owner.PhysicalShardId);
        ResourceRetirementCompletionValidation.Release(body.OwnerRelease, Limits);
        if (state.Stage != ResourceRetirementStage.CatalogFinalized
            || !ResourceRetirementValidation.Same(body.OwnerRelease.Finalization, state.Finalization)
            || state.OwnerReleaseReceipts.Any(value => value.Receiver.Owner.PhysicalShardId
                == body.OwnerRelease.Receiver.Owner.PhysicalShardId))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }

        state = state with
        {
            OwnerReleaseReceipts = state.OwnerReleaseReceipts.Add(body.OwnerRelease)
        };
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        return new(RetirementReceipt(view, body.Control, operation, phase.Phase, position), state, null);
    }

    private ResourceRetirementPhaseResultV1 FinishResourceRetirement(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementFinishBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        var state = RequireResourceRetirementFinalizedSource(view, body.Control);
        RequireRetirementPhase(operation, phase, body.Control, body.Control.Catalog.DefaultShard.PhysicalShardId);
        if (state.Stage != ResourceRetirementStage.CatalogFinalized
            || state.OwnerReleaseReceipts.Length != state.Control.Directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }

        var source = state.OwnerReleaseReceipts.Single(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Receiver.Owner, state.Control.Catalog.DefaultShard));
        var marker = ResourceRetirementConservationReleaseStorage.Read(view, state.Control.Lifetime,
            source.Receiver.Owner, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (!ResourceRetirementValidation.Same(marker.OwnerRelease, source))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var receipt = RetirementReceipt(view, body.Control, operation, phase.Phase, position);
        state = state with
        {
            Stage = ResourceRetirementStage.Completed,
            CompletionReceipt = receipt,
            CompletionOperation = operation,
            CompletionPolicyEpoch = principal.PolicyEpoch
        };
        ResourceRetirementCompletionValidation.RequireCompleted(state, Limits);
        ResourceRetirementStorage.ReleaseBlocker(transaction, body.Control.Lifetime, Limits);
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        return new(receipt, state, null);
    }
}
