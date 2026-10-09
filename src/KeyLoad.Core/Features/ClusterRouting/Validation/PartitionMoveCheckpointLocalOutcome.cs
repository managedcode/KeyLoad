using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult RequireCheckpointLocalOutcome(IKeyValueView view, PrincipalRecord principal,
        PartitionMoveParentPhase pending, OperationResult presented)
    {
        var actual = ReadCheckpointLocalOutcome(view, principal, pending)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (NativeSerialization.Measure(presented) > Limits.MaxBatchBytes
            || !NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(presented)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return actual;
    }

    private OperationResult? ReadCheckpointLocalOutcome(IKeyValueView view, PrincipalRecord principal,
        PartitionMoveParentPhase pending)
    {
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        RequireMoveParentOriginalIssuance(view, principal, pending);
        var original = pending.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (!PartitionMoveGrantValidation.IsLocalControl(original.Stage))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, pending.Partition);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, pending.OriginalPhaseCommandId, scope);
        var actual = selected.Outcome;
        if (actual is null)
        { return null; }
        var identity = MovementPhaseIdentityJson(original);
        var fingerprint = CommandFingerprint(new(pending.OriginalPhaseCommandId,
            OperationKind.PartitionMovementPhase, principal.Id, default, identity));
        if (actual.Incarnation != Store.Identity.Incarnation || actual.PolicyEpoch != pending.OriginalIssuancePolicyEpoch
            || actual.Fingerprint != fingerprint)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return actual.Result;
    }
}
