using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteResourceRetirementPreparation(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
        => phase.Phase switch
        {
            ResourceRetirementPhase.PrepareRetirement => PrepareResourceRetirement(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.ReserveOwner => ReserveResourceRetirementOwner(transaction, operation, phase, position),
            ResourceRetirementPhase.RecordReservation => RecordResourceRetirementReservation(transaction, operation, phase, position),
            _ => throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid)
        };

    private ResourceRetirementPhaseReceiptV1 PreparationReceipt(IKeyValueView view,
        ResourceRetirementPreparationControlV1 control, ReplicatedOperation operation, ResourceRetirementPhase phase, long position)
        => new(ResourceRetirementProtocol.Version, control.Request.RetirementId, phase, operation.Id,
            control.Lifetime, ReadInventoryPhysicalCatalog(view).DefaultShard, position, control.IntentDigest);
}
