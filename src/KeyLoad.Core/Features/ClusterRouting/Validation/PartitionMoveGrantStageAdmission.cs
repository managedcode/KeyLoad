using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveGrantStageAdmission
{
    internal static void Require(IKeyValueView view, PartitionMovePhaseCommand phase, PartitionMoveControlRecord control, int maximumBytes)
    {
        var admitted = phase.Stage switch
        {
            PartitionMovePeerStage.ControlApplyCommand => control.Phase == PartitionMovePhase.Retired,
            PartitionMovePeerStage.Fence => control.Phase == PartitionMovePhase.Prepared,
            PartitionMovePeerStage.Capture => control.Phase == PartitionMovePhase.Fenced,
            PartitionMovePeerStage.StagePage or PartitionMovePeerStage.Install
                => control.Phase == PartitionMovePhase.Captured,
            PartitionMovePeerStage.PublishWitness or PartitionMovePeerStage.Retire
                => control.Phase == PartitionMovePhase.Published,
            PartitionMovePeerStage.SourceBeginAbort => control.Phase == PartitionMovePhase.Aborting,
            PartitionMovePeerStage.Abort => control.Phase == PartitionMovePhase.Aborting,
            _ => false
        };
        if (!admitted)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (phase.Stage is PartitionMovePeerStage.Abort or PartitionMovePeerStage.Retire or PartitionMovePeerStage.SourceBeginAbort)
        { RequireCleanup(view, phase, control, maximumBytes); }
        if (phase.Stage == PartitionMovePeerStage.ControlApplyCommand)
        { PartitionControlEffectGrantAdmission.Require(view, phase, control, maximumBytes); }
        if (phase.Stage == PartitionMovePeerStage.PublishWitness)
        { RequirePublication(view, phase, control); }
        if (phase.Stage == PartitionMovePeerStage.Fence)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveControlBody>(phase.Body.Span);
            if (body.OperatorPrincipalId != control.PrincipalId
                || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(control)
                || PartitionMoveIntentIdentity.Digest(body.Control) != phase.ControlIntentDigest)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        }
    }

    private static void RequireCleanup(IKeyValueView view, PartitionMovePhaseCommand phase, PartitionMoveControlRecord control, int maximumBytes)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        if (body.OperatorPrincipalId != control.PrincipalId
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(control)
            || !Enum.IsDefined(body.Role) || body.FamilyOrdinal < PartitionMoveProtocol.EmptyCount
            || body.FamilyOrdinal > PartitionMoveCleanupFamilies.All.Length
            || phase.Stage is PartitionMovePeerStage.Retire or PartitionMovePeerStage.SourceBeginAbort
                && body.Role != PartitionMoveCleanupRole.Source)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (phase.Stage == PartitionMovePeerStage.Retire)
        {
            var publication = PartitionMovePublishedPlacementStorage.Read(view, control.Partition)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            if (body.Publication is null
                || JsonData.Fingerprint(body.Publication) != JsonData.Fingerprint(publication))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        }
        if (body.Role == PartitionMoveCleanupRole.Source)
        { RequirePrecedingCleanup(view, body, phase, maximumBytes); }
    }

    private static void RequirePrecedingCleanup(IKeyValueView view, PartitionMoveCleanupBody body,
        PartitionMovePhaseCommand phase, int maximumBytes)
    {
        var grant = PartitionMoveGrantStorage.Read(view, phase.Partition, body.PrecedingGrantId,
            maximumBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.MoveId != phase.MoveId || grant.Settlement is null || grant.AbortDisposition is not null
            || grant.RetireCancellationDisposition is not null
            || grant.ControlIntentDigest != phase.ControlIntentDigest
            || phase.Stage == PartitionMovePeerStage.Retire && grant.Stage != PartitionMovePeerStage.PublishWitness
            || phase.Stage is PartitionMovePeerStage.Abort or PartitionMovePeerStage.SourceBeginAbort
                && (grant.Stage != PartitionMovePeerStage.Abort || grant.CleanupRole != PartitionMoveCleanupRole.Target
                    || grant.CleanupFamily != PartitionMoveCleanupFamilies.All.Length))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private static void RequirePublication(IKeyValueView view, PartitionMovePhaseCommand phase,
        PartitionMoveControlRecord control)
    {
        var body = NativeSerialization.Deserialize<PartitionMovePublishBody>(phase.Body.Span);
        var publication = PartitionMovePublishedPlacementStorage.Read(view, control.Partition)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (body.OperatorPrincipalId != control.PrincipalId
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(control)
            || JsonData.Fingerprint(body.Publication) != JsonData.Fingerprint(publication))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
