using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlEffectGrantAdmission
{
    internal static void Require(IKeyValueView view, PartitionMovePhaseCommand phase,
        PartitionMoveControlRecord control, int maximumBytes)
    {
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
        var delegation = body.Delegation;
        var record = PartitionControlCommandStorage.Read(view, delegation.Identity, maximumBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (record.Phase != PartitionControlCommandPhase.Admitted || record.Delegation is null
            || body.OperatorPrincipalId != control.PrincipalId || delegation.MoveId != control.MoveId
            || delegation.Identity.Partition != phase.Partition
            || JsonData.Fingerprint(record.Delegation) != JsonData.Fingerprint(delegation)
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(control)
            || record.EffectId != delegation.EffectId
            || !record.TargetBody.Span.SequenceEqual(phase.Body.Span))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
