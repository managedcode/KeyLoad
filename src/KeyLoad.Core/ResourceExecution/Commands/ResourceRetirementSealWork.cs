using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 SealResourceRetirementWork(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementSealAllWorkBodyV1>(phase.Body.Span);
        var state = RequireRetirementSource(transaction, body.Control);
        RequireRetirementPhase(operation, phase, body.Control, body.Control.Catalog.DefaultShard.PhysicalShardId);
        if (state.WorkBarrierReceipt is { } existing)
        {
            ResourceRetirementWorkValidation.RequireBarrier(state, Limits);
            return new(existing.Receipt, ReadOriginalResourceRetirementWorkBarrier(transaction, state), null)
            {
                WorkBarrierReceipt = existing
            };
        }
        if (state.Stage != ResourceRetirementStage.BarrierSealed || state.SealedReceipt is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        var receipt = RetirementReceipt(transaction, body.Control, operation, phase.Phase, position);
        var barrier = new ResourceRetirementWorkBarrierReceiptV1(body.Control, state.SealedReceipt,
            state.WorkOwnerReceipts, receipt)
        {
            Original = operation,
            PolicyEpoch = principal.PolicyEpoch
        };
        state = state with
        {
            WorkBarrierReceipt = barrier
        };
        ResourceRetirementWorkValidation.RequireBarrier(state, Limits);
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        return new(receipt, state, null)
        {
            WorkBarrierReceipt = barrier
        };
    }
}
