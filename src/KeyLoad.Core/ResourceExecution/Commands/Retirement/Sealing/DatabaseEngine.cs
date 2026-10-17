using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 FenceResourceRetirement(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementFenceBodyV1>(phase.Body.Span);
        if (body is null || !PhysicalOwnerEntryValidation.Valid(body.Receiver))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementValidation.Control(body.Control, Limits);
        RequireRetirementPhase(operation, phase, body.Control, body.Receiver.Owner.PhysicalShardId);
        RequireRetirementReceiver(transaction, body.Control, body.Receiver);
        var existing = ResourceRetirementStorage.ReadFence(transaction, body.Control.Lifetime, Limits);
        if (existing is not null)
        {
            RequireSameRetirement(existing.Control, body.Control);
            return new(existing.Receipt, null, existing);
        }
        var reservation = RequireRetirementReservationForFence(transaction, body.Control);
        var receipt = RetirementReceipt(transaction, body.Control, operation, phase.Phase, position);
        var fence = new ResourceRetirementOwnerFenceV1(body.Control, receipt);
        ResourceRetirementStorage.WriteFence(transaction, fence, Limits);
        ResourceRetirementStorage.CreateBlocker(transaction, body.Control.Lifetime, Limits);
        ResourceRetirementPreparationStorage.CompleteOwner(transaction, reservation, Limits);
        return new(receipt, null, fence);
    }

    private void RequireRetirementReceiver(IKeyValueView view, ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 receiver)
    {
        var local = ReadInventoryPhysicalCatalog(view).DefaultShard;
        var registered = ResourceRetirementStateValidation.Member(control, local);
        if (!PhysicalOwnerEntryValidation.Same(registered, receiver))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }

    private static void RequireSameRetirement(ResourceRetirementControlV1 actual, ResourceRetirementControlV1 expected)
    {
        if (!ResourceRetirementValidation.Same(actual, expected))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
    }
}
