using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 CommitResourceRetirementWork(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementCommitOwnerWorkJoinBodyV1>(phase.Body.Span);
        ResourceRetirementStateValidation.State(body.SealedState, Limits);
        if (body.SealedState.Stage != ResourceRetirementStage.BarrierSealed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        var control = body.SealedState.Control;
        RequireRetirementPhase(operation, phase, control, body.Receiver.Owner.PhysicalShardId);
        RequireRetirementReceiver(transaction, control, body.Receiver);
        var fence = ResourceRetirementStorage.ReadFence(transaction, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(fence.Control, control);
        var receipt = new ResourceRetirementWorkOwnerReceiptV1(control, body.Receiver, fence, body.Evidence,
            RetirementReceipt(transaction, control, operation, phase.Phase, position));
        ResourceRetirementWorkValidation.Owner(receipt, control, Limits);
        var existing = ResourceRetirementWorkStorage.ReadOwner(transaction, control, Limits);
        if (existing is not null)
        {
            if (!ResourceRetirementValidation.Same(existing.Fence, fence)
                || !ResourceRetirementValidation.Same(existing.Evidence, body.Evidence))
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            return new(existing.Receipt, null, null)
            {
                WorkOwnerReceipt = existing
            };
        }
        ResourceRetirementWorkStorage.WriteOwner(transaction, receipt, Limits);
        return new(receipt.Receipt, null, null)
        {
            WorkOwnerReceipt = receipt
        };
    }
}
