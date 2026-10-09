using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveCheckpoint(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase,
        DateTimeOffset evaluatedAt, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(phase.Body.Span);
        var previous = PartitionMoveParentStorage.Header(transaction, phase.Partition, phase.MoveId, Limits.MaxBatchBytes);
        var header = previous ?? CreateMoveParentHeader(transaction, principal, body, phase);
        RequireMoveParentCheckpointScope(transaction, principal, phase, body, header);
        var receipt = MoveJournalReceipt(transaction, commandId, position, phase.ControlIntentDigest);
        switch (body.Action)
        {
            case PartitionMoveCheckpointAction.Admit:
                var admitted = AdmitMoveParentPhase(transaction, principal, body, header, receipt, evaluatedAt);
                header = SaveMoveParentAdmission(transaction, previous, header, admitted);
                break;
            case PartitionMoveCheckpointAction.Observe:
                header = body.ObservedOriginalResult is null
                    ? ObserveMoveParentProof(transaction, body, header, receipt)
                    : ObserveMoveParentOutcome(transaction, principal, body, header, receipt);
                break;
            case PartitionMoveCheckpointAction.CompactTerminal:
                header = CompactMoveParentTerminal(transaction, principal, body, header, receipt);
                break;
            case PartitionMoveCheckpointAction.CancelUnprepared:
                header = CancelUnpreparedMoveParent(transaction, principal, body, header, phase, receipt);
                break;
            case PartitionMoveCheckpointAction.ObserveCancellation:
                header = ObserveMoveParentCancellation(transaction, principal, body, header, receipt);
                break;
            case PartitionMoveCheckpointAction.AdmitRetireCancellation:
                header = AdmitMoveParentRetireCancellation(transaction, body, header, receipt);
                break;
            case PartitionMoveCheckpointAction.ObserveRetireCancellation:
                header = ObserveMoveParentRetireCancellation(transaction, body, header, receipt);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid);
        }
        return new(header.MoveId, phase.Stage, receipt,
            PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition, phase.MoveId, Limits.MaxBatchBytes),
            null, null, null);
    }

    private void RequireMoveParentCheckpointScope(IKeyValueView view, PrincipalRecord principal,
        PartitionMovePhaseCommand phase, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header)
    {
        if (!principal.ClusterAdministrator || body.Version != PartitionMoveProtocol.Version
            || body.OperatorPrincipalId != principal.Id || !Enum.IsDefined(body.Action)
            || body.ExpectedGeneration != header.Generation || header.TerminalResult is not null
            || body.Action != PartitionMoveCheckpointAction.ObserveRetireCancellation && body.RetireCancellation is not null
            || body.Action != PartitionMoveCheckpointAction.AdmitRetireCancellation && body.RetireCancellationAttempt is not null
            || phase.MoveId != header.MoveId || phase.Partition != header.Partition
            || !PhysicalOwnerEntryValidation.SameOwner(phase.ControlOwner, header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(phase.DestinationOwner, header.DestinationOwner)
            || !PartitionMoveControlValidation.SameSource(phase.SourcePlacement, header.SourcePlacement)
            || phase.GrantId is not null || phase.Resources.IsDefault || !phase.Resources.IsEmpty)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveParentValidation.RequireReplayScope(header, principal.Id, body.OriginalTransferRequest);
        var directory = RequireMoveDirectory(view);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, header.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var control = PartitionMoveControlStorage.ReadHistory(view, header.Partition, header.MoveId, Limits.MaxBatchBytes);
        var digest = control is null
            ? body.OriginalPhase?.ControlIntentDigest ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
            : PartitionMoveIntentIdentity.Digest(control);
        if (phase.ControlIntentDigest != digest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
