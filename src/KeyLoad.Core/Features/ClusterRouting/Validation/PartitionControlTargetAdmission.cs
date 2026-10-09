using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private (ReplicatedOperation Original, CommandRequest? Command) RequireControlledDocumentTarget(
        IKeyValueView view, PartitionMovePhaseCommand phase, PartitionControlApplyBody body,
        Guid effectId, DateTimeOffset now)
    {
        var delegation = body.Delegation;
        var original = delegation.OriginalOperation
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var expected = new PartitionControlCommandIdentity(scope.Kind, scope.Partition, original.PrincipalId, original.Id);
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(phase.Partition), Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        if (!stage.Published || body.Control.Phase != PartitionMovePhase.Retired
            || stage.Control.MoveId != phase.MoveId || delegation.MoveId != phase.MoveId
            || expected != delegation.Identity || expected.ScopeKind != CommandOutcomeScopeKind.Partition
            || expected.Partition != phase.Partition || delegation.EffectId != effectId
            || delegation.ControlAdmissionPosition <= PartitionMoveProtocol.EmptyCount
            || delegation.ExpiresAt <= now || delegation.Principal.Revoked || delegation.Principal.ExpiresAt <= now
            || delegation.Principal.Id != expected.PrincipalId || delegation.Principal.PolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || delegation.OriginalKind != original.Kind || delegation.OriginalPayloadJson != original.PayloadJson
            || delegation.Fingerprint != CommandFingerprint(original)
            || delegation.ControlOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(delegation.ControlOwner, phase.ControlOwner)
            || body.OperatorPrincipalId != body.Control.PrincipalId
            || PartitionMoveIntentIdentity.Digest(body.Control) != phase.ControlIntentDigest
            || PartitionMoveIntentIdentity.Digest(stage.Control) != phase.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var placement = ReadPlacementWitness(view, phase.Partition);
        if (JsonData.Fingerprint(placement) != JsonData.Fingerprint(delegation.TargetPlacement)
            || placement.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireControlEffectResources(phase, delegation, stage);
        var command = RequireControlledCommandBody(original, phase.Partition);
        if (JsonData.Fingerprint(command) != JsonData.Fingerprint(body.Command))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        return (original, command);
    }

    private static void RequireControlEffectResources(PartitionMovePhaseCommand phase,
        PartitionControlDelegation delegation, PartitionMoveTargetStage stage)
    {
        if (delegation.Resources.IsDefault || phase.Resources.IsDefault || stage.Descriptor.Resources.IsDefault
            || JsonData.Fingerprint(delegation.Resources) != JsonData.Fingerprint(phase.Resources))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        foreach (var resource in delegation.Resources)
        {
            var original = stage.Descriptor.Resources.SingleOrDefault(value => value.Name == resource.Name);
            if (original is null || !ResourcePolicyUpdates.SameNonPolicyDefinition(original, resource))
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.InvalidImage); }
        }
    }
}
