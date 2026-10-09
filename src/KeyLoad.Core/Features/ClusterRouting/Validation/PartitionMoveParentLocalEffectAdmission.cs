using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Parent local effect execution always uses its first actual policy epoch, including after a restore.</summary>
    private bool RequireMoveParentLocalEffectAdmission(IKeyValueView view, PrincipalRecord principal,
        Guid commandId, PartitionMovePhaseCommand phase)
    {
        if (!PartitionMoveGrantValidation.IsLocalControl(phase.Stage)
            || phase.Stage == PartitionMovePeerStage.ControlCheckpoint)
        { return false; }
        var header = PartitionMoveParentStorage.Header(view, phase.Partition, phase.MoveId, Limits.MaxBatchBytes);
        if (header is null)
        {
            var originalControl = PartitionMoveControlStorage.ReadHistory(view, phase.Partition,
                phase.MoveId, Limits.MaxBatchBytes);
            if (originalControl is { ParentCheckpointRequired: true } && originalControl.PrincipalId != principal.Id)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            if (view.ReadOwnedValue(PartitionMoveParentKeys.Active(phase.Partition)) is not null
                || PartitionMoveParentStorage.HasPhase(view, phase.Partition, phase.MoveId)
                || originalControl is { ParentCheckpointRequired: true })
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            return false;
        }
        if (!principal.ClusterAdministrator || header.OperatorPrincipalId != principal.Id)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        var first = PartitionMoveParentStorage.Phase(view, phase.Partition, phase.MoveId, commandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (first.OriginalIssuancePolicyEpoch != principal.PolicyEpoch || first.OriginalPhase is null
            || first.OriginalGrant is not null
            || first.OriginalResult is null && (header.TerminalResult is not null
                || header.PendingOriginalPhaseCommandId != commandId)
            || !PhysicalOwnerEntryValidation.SameOwner(header.ControlOwner, phase.ControlOwner)
            || !NativeSerialization.Serialize(first.OriginalPhase).AsSpan().SequenceEqual(NativeSerialization.Serialize(phase)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMoveParentOriginalIssuance(view, principal, first);
        if (first.OriginalResult is not null)
        { _ = RequireCheckpointLocalOutcome(view, principal, first, first.OriginalResult); }
        return true;
    }
}
