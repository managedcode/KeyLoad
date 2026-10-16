using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 RecordResourceRetirementOwnerCleanup(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerCleanupBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        var state = RequireRetirementSource(view, body.Control);
        ResourceRetirementWorkValidation.RequireBarrier(state, Limits);
        RequireRetirementPhase(operation, phase, body.Control, body.OwnerCleanup.Receiver.Owner.PhysicalShardId);
        ResourceRetirementFinalizationValidation.Owner(body.OwnerCleanup, state, Limits);
        if (state.OwnerCleanupReceipts.Any(value => value.Receiver.Owner.PhysicalShardId
            == body.OwnerCleanup.Receiver.Owner.PhysicalShardId))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }

        state = state with
        {
            OwnerCleanupReceipts = state.OwnerCleanupReceipts.Add(body.OwnerCleanup)
        };
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        return new(RetirementReceipt(view, body.Control, operation, phase.Phase, position), state, null);
    }

    private ResourceRetirementPhaseResultV1 FinalizeResourceRetirementCatalog(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementFinalizeCatalogBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        var state = RequireRetirementSource(view, body.Control);
        ResourceRetirementWorkValidation.RequireBarrier(state, Limits);
        RequireRetirementPhase(operation, phase, body.Control, body.Control.Catalog.DefaultShard.PhysicalShardId);
        if (ResourceRetirementFinalizationStorage.ReadHistory(view, body.Control.Lifetime, Limits) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        // The evidence embeds the original phase12 snapshot, never this growing D5 progress record.
        var sealedState = ReadOriginalResourceRetirementWorkBarrier(view, state);
        var receipt = RetirementReceipt(view, body.Control, operation, phase.Phase, position);
        var canonical = new CommitReceipt(operation.Id,
            new(body.Control.Catalog.DefaultShard.Incarnation, NativeCatalogRoute.PartitionId,
                position, body.Control.Catalog.DefaultShard.PlacementEpoch), [], Durability);
        var completed = new ResourceRetirementFinalizationV1(body.Control,
            new(sealedState, state.OwnerCleanupReceipts), receipt)
        {
            CanonicalReceipt = canonical,
            Original = operation,
            PolicyEpoch = principal.PolicyEpoch
        };
        ResourceRetirementFinalizationValidation.Require(completed, Limits);
        ResourceRetirementFinalizationStorage.Finalize(transaction, completed, Limits);
        state = state with
        {
            Stage = ResourceRetirementStage.CatalogFinalized,
            Finalization = completed
        };
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        return new(receipt, state, null)
        {
            Finalization = completed
        };
    }
}
