using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 RecordResourceRetirementWork(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerWorkJoinBodyV1>(phase.Body.Span);
        var state = RequireRetirementSource(transaction, body.Control);
        ResourceRetirementWorkValidation.Owner(body.OwnerReceipt, body.Control, Limits);
        var owner = body.OwnerReceipt.Receiver.Owner;
        RequireRetirementPhase(operation, phase, body.Control, owner.PhysicalShardId);
        if (state.Stage != ResourceRetirementStage.BarrierSealed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        var previous = state.WorkOwnerReceipts.SingleOrDefault(item =>
            item.Receiver.Owner.PhysicalShardId == owner.PhysicalShardId);
        if (previous is not null && !ResourceRetirementValidation.Same(previous, body.OwnerReceipt))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        if (previous is null)
        {
            if (state.WorkBarrierReceipt is not null)
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            state = state with
            {
                WorkOwnerReceipts = state.WorkOwnerReceipts.Add(body.OwnerReceipt)
            };
            ResourceRetirementStorage.WriteState(transaction, state, Limits);
        }
        return new(RetirementReceipt(transaction, body.Control, operation, phase.Phase, position), state, null);
    }
}
