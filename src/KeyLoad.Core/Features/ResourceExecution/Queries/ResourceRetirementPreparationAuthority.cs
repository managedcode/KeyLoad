using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPreparationStateV1 RequirePreparationSource(IKeyValueView view,
        ResourceRetirementPreparationControlV1 control)
    {
        ResourceRetirementPreparationValidation.Control(control, Limits);
        var state = ResourceRetirementPreparationStorage.ReadPreparation(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
        if (!ResourceRetirementValidation.Same(state.Control, control)
            || !ResourceRetirementValidation.Same(ReadInventoryPhysicalCatalog(view), control.Catalog)
            || !ResourceRetirementValidation.Same(PhysicalOwnerDirectorySerialization.Read(view), control.Directory))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        RequirePreparationOutcome(view, control);
        return state;
    }

    private void RequirePreparationOutcome(IKeyValueView view, ResourceRetirementPreparationControlV1 control)
        => ResourceRetirementPreparationEvidence.Source(view, control, Limits);

    private void RequirePreparationReceiver(IKeyValueView view, ResourceRetirementPreparationControlV1 control,
        RegisteredPhysicalOwnerV1 receiver)
    {
        ResourceRetirementPreparationValidation.Control(control, Limits);
        ResourceRetirementPreparationValidation.Member(control, receiver);
        if (!KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(
            ReadInventoryPhysicalCatalog(view).DefaultShard, receiver.Owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }
}
