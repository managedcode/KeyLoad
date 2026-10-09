using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireCheckpointProofAdmission(string principalId, PartitionMovePeerEnvelope envelope,
        ReadExecutionBudget work)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(envelope.Body.Span);
        if (body.Action is not (PartitionMoveCheckpointAction.Observe
            or PartitionMoveCheckpointAction.CancelUnprepared or PartitionMoveCheckpointAction.ObserveCancellation
            or PartitionMoveCheckpointAction.ObserveRetireCancellation or PartitionMoveCheckpointAction.AdmitRetireCancellation))
        { return; }
        Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = RequireMoveDispatchPrincipal(charged, principalId, EvaluationClock.GetUtcNow());
            var header = PartitionMoveParentStorage.Header(charged, envelope.Partition, envelope.MoveId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var phase = new PartitionMovePhaseCommand(envelope.Version, envelope.MoveId, envelope.Partition,
                envelope.ControlOwner, envelope.SourcePlacement, envelope.DestinationOwner,
                envelope.ControlIntentDigest, envelope.Stage, envelope.PageOrdinal, envelope.Body,
                envelope.Grant?.GrantId, envelope.Grant?.Resources ?? System.Collections.Immutable.ImmutableArray<ResourceDefinition>.Empty);
            RequireMoveParentCheckpointScope(charged, principal, phase, body, header);
            if (body.Action is PartitionMoveCheckpointAction.ObserveRetireCancellation
                or PartitionMoveCheckpointAction.AdmitRetireCancellation)
            {
                RequireMoveParentRetireCheckpointProof(charged, body, header);
                return true;
            }
            if (body.Action == PartitionMoveCheckpointAction.CancelUnprepared)
            {
                RequireUnpreparedMoveParentCancellation(charged, principal, body, header);
                return true;
            }
            if (body.Action == PartitionMoveCheckpointAction.ObserveCancellation)
            {
                RequireObservedMoveParentCancellation(charged, principal, body, header);
                return true;
            }
            var original = PartitionMoveParentStorage.Phase(charged, header.Partition, header.MoveId,
                body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            RequireCheckpointObservedProofShape(charged, principal, body, header, original);
            return true;
        });
        work.Check();
    }

    private void RequireCheckpointObservedProofShape(KeyLoad.Storage.IKeyValueView view, PrincipalRecord principal,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveParentPhase original)
    {
        if (body.ObservedOriginalResult is null)
        {
            RequireMoveParentProof(body, header, original);
            return;
        }
        RequireMoveParentObservation(body, header, original);
        var actual = PartitionMoveGrantValidation.IsLocalControl(original.Stage)
            ? RequireCheckpointLocalOutcome(view, principal, original, body.ObservedOriginalResult)
            : body.ObservedOriginalResult;
        if (actual.Error is null)
        { RequireMoveParentResultIdentity(header, original, actual.Get<PartitionMovePhaseResult>()); }
        if (body.NextOriginalPhase is null)
        { return; }
        var grant = RequireMoveParentGrantPromotion(principal, body, original, actual);
        if (PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId, grant.PhaseCommandId,
            Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }
}
