using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveUnissuedAuthorizationAbort(IKeyValueView view, PrincipalRecord principal,
        PartitionMoveParentHeader header, PartitionMoveParentPhase interrupted, PartitionMoveControlRecord control)
    {
        if (!principal.ClusterAdministrator || principal.Id != header.OperatorPrincipalId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        var original = interrupted.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (control.Phase != PartitionMovePhase.Aborted || original.Stage != PartitionMovePeerStage.ControlAuthorize
            || interrupted.OriginalResult is not null || interrupted.ObservationCheckpointReceipt is not null
            || interrupted.OriginalGrant is not null || interrupted.OriginalAuthorization is not null
            || interrupted.OriginalRequestNonce != Guid.Empty || interrupted.OriginalCaptureReleaseNonce != Guid.Empty
            || original.GrantId is not null || original.MoveId != header.MoveId || original.Partition != header.Partition
            || !PhysicalOwnerEntryValidation.SameOwner(original.ControlOwner, header.ControlOwner)
            || !PartitionMoveControlValidation.SameSource(original.SourcePlacement, header.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(original.DestinationOwner, header.DestinationOwner))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.Body.Span);
        if (body.GrantId != interrupted.OriginalPhaseCommandId || body.PhaseCommandId == Guid.Empty
            || body.PhaseCommandId == body.GrantId || body.OperatorPrincipalId != principal.Id
            || body.ExpiresAt != interrupted.OriginalExpiresAt || body.Phase.GrantId is not null
            || PartitionMoveGrantValidation.IsLocalControl(body.Phase.Stage)
            || body.Phase.MoveId != header.MoveId || body.Phase.Partition != header.Partition
            || body.Phase.ControlIntentDigest != original.ControlIntentDigest
            || !MatchesMoveReceiver(body.Phase, body.ReceiverOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Phase.ControlOwner, header.ControlOwner)
            || !PartitionMoveControlValidation.SameSource(body.Phase.SourcePlacement, header.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Phase.DestinationOwner, header.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        if (ReadCheckpointLocalOutcome(view, principal, interrupted) is not null
            || PartitionMoveGrantStorage.Read(view, header.Partition, body.GrantId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var terminal = PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId,
            header.LastOriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (terminal.Stage != PartitionMovePeerStage.ControlFinalizeAbort || terminal.OriginalResult is null
            || terminal.ObservationCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var actual = RequireCheckpointLocalOutcome(view, principal, terminal, terminal.OriginalResult)
            .Get<PartitionMovePhaseResult>();
        var applied = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (NativeSerialization.Deserialize<long>(applied) < actual.Journal.AppliedPosition
            || actual.Stage != PartitionMovePeerStage.ControlFinalizeAbort || actual.Control is null
            || actual.Journal.CommandId != terminal.OriginalPhaseCommandId || actual.Journal.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, header.ControlOwner)
            || !NativeSerialization.Serialize(actual.Control).AsSpan().SequenceEqual(NativeSerialization.Serialize(control)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        RequireNoUnsettledMoveGrants(view, control);
        // The genuine closed Abort barrier prevents any later original authorization, without inventing its result.
    }
}
