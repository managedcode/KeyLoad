using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 RecordResourceRetirementReceipt(IAtomicTransaction transaction,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementReceiptBodyV1>(phase.Body.Span);
        if (body is null || body.OwnerReceipt is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var state = RequireRetirementSource(transaction, body.Control);
        var member = ResourceRetirementStateValidation.Member(body.Control, body.OwnerReceipt.PhysicalOwner);
        ResourceRetirementValidation.Receipt(body.OwnerReceipt, body.Control,
            ResourceRetirementPhase.FenceOwner, member.Owner, Limits);
        RequireRetirementPhase(operation, phase, body.Control, member.Owner.PhysicalShardId);
        var previous = state.OwnerReceipts.SingleOrDefault(receipt =>
            PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, member.Owner));
        if (previous is not null && !ResourceRetirementValidation.Same(previous, body.OwnerReceipt))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        if (previous is null)
        {
            if (state.Stage != ResourceRetirementStage.Retiring)
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            state = state with
            {
                OwnerReceipts = state.OwnerReceipts.Add(body.OwnerReceipt)
            };
            ResourceRetirementStorage.WriteState(transaction, state, Limits);
        }
        return new(RetirementReceipt(transaction, body.Control, operation, phase.Phase, position), state, null);
    }

    private ResourceRetirementStateV1 RequireRetirementSource(IKeyValueView view, ResourceRetirementControlV1 control)
    {
        ResourceRetirementValidation.Control(control, Limits);
        var state = ResourceRetirementStorage.ReadControl(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
        RequireSameRetirement(state.Control, control);
        var catalog = ReadInventoryPhysicalCatalog(view);
        if (!ResourceRetirementValidation.Same(catalog, control.Catalog)
            || !ResourceRetirementValidation.Same(
                KeyLoad.Core.Features.ClusterRouting.Serialization.PhysicalOwnerDirectorySerialization.Read(view), control.Directory)
            || control.PlacementDirectoryRevision != ResourceOwnerInventoryReadValidation.EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        var (definition, lifetime) = ReadInventoryResourcePair(view, control.Request.TenantId,
            control.Request.DatabaseId, control.Request.ResourceName, initial: false);
        if (!ResourceRetirementValidation.Same(definition, control.Definition)
            || !ResourceRetirementValidation.Same(lifetime, control.Lifetime))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        RequireRetirementBeginOutcome(view, control);
        return state;
    }
}
