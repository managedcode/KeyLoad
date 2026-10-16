using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteResourceRetirementPhase(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, long position)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ResourceRetirementProtocol.Administrator);
        }
        var phase = Payload<ResourceRetirementPhaseCommandV1>(operation);
        if (phase is null || phase.Version != ResourceRetirementProtocol.Version || phase.RetirementId == Guid.Empty
            || !Enum.IsDefined(phase.Phase) || phase.Body.IsEmpty || phase.Body.Length > Limits.MaxBatchBytes
            || position <= ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementValidation.Bound(phase, Limits);
        if (phase.Phase is ResourceRetirementPhase.PrepareRetirement or ResourceRetirementPhase.ReserveOwner
            or ResourceRetirementPhase.RecordReservation)
        {
            return ExecuteResourceRetirementPreparation(transaction, principal, operation, phase, position);
        }
        if (phase.Phase == ResourceRetirementPhase.PreparedOperation)
        {
            return ExecuteResourceRetirementPreparedOperation(transaction, principal, operation, phase, position);
        }
        if (phase.Phase == ResourceRetirementPhase.SealBarrier)
        {
            return SealResourceRetirement(transaction, principal, operation, phase, position);
        }
        var result = phase.Phase switch
        {
            ResourceRetirementPhase.Begin => BeginResourceRetirement(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.FenceOwner => FenceResourceRetirement(transaction, operation, phase, position),
            ResourceRetirementPhase.RecordOwnerReceipt => RecordResourceRetirementReceipt(transaction, operation, phase, position),
            ResourceRetirementPhase.PrepareAttempt => PrepareResourceRetirementAttempt(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.CommitOwnerWorkJoin => CommitResourceRetirementWork(transaction, operation, phase, position),
            ResourceRetirementPhase.RecordOwnerWorkJoin => RecordResourceRetirementWork(transaction, operation, phase, position),
            ResourceRetirementPhase.SealAllWork => SealResourceRetirementWork(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.CleanupPage => ExecuteResourceRetirementCleanupPage(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.CompleteOwnerCleanup => CompleteResourceRetirementOwnerCleanup(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.RecordOwnerCleanup => RecordResourceRetirementOwnerCleanup(transaction, operation, phase, position),
            ResourceRetirementPhase.FinalizeCatalog => FinalizeResourceRetirementCatalog(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.ReleaseOwnerTopology => ReleaseResourceRetirementOwnerTopology(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.RecordOwnerRelease => RecordResourceRetirementOwnerRelease(transaction, operation, phase, position),
            ResourceRetirementPhase.Finish => FinishResourceRetirement(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.PersistDropParent => PersistResourceRetirementDropParent(transaction, principal, operation, phase, position),
            ResourceRetirementPhase.CompleteDropParent => CompleteResourceRetirementDropParent(transaction, principal, operation, phase, position),
            _ => throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid)
        };
        ResourceRetirementValidation.Bound(result, Limits);
        return Result(result);
    }

    private ResourceRetirementPhaseReceiptV1 RetirementReceipt(IKeyValueView view, ResourceRetirementControlV1 control,
        ReplicatedOperation operation, ResourceRetirementPhase phase, long position)
        => new(ResourceRetirementProtocol.Version, control.Request.RetirementId, phase, operation.Id,
            control.Lifetime, ReadInventoryPhysicalCatalog(view).DefaultShard, position, control.IntentDigest);

    private static void RequireRetirementPhase(ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase,
        ResourceRetirementControlV1 control, Guid owner)
    {
        if (phase.RetirementId != control.Request.RetirementId
            || operation.Id != ResourceRetirementIdentity.PhaseCommandId(control.Request, phase.Phase, owner))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
    }
}
