using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader CompactMoveParentTerminal(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header,
        PartitionMoveJournalReceipt receipt)
    {
        var last = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            header.LastOriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (header.PendingOriginalPhaseCommandId is not null || last.OriginalResult is null || last.OriginalResult.Error is not null
            || last.ObservationCheckpointReceipt is null || body.OriginalPhaseCommandId != last.OriginalPhaseCommandId
            || body.ObservedOriginalResult is null || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.OriginalExpiresAt != last.OriginalExpiresAt || body.OriginalRequestNonce != last.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != last.OriginalCaptureReleaseNonce
            || body.NextOriginalPhase is not null || body.NextOriginalPhaseCommandId is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var actual = last.OriginalResult.Get<PartitionMovePhaseResult>();
        var control = PartitionMoveControlStorage.ReadHistory(transaction, header.Partition, header.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var expectedStage = control.Phase == PartitionMovePhase.Aborted
            ? PartitionMovePeerStage.ControlFinalizeAbort : PartitionMovePeerStage.ControlCompleteRetirement;
        if (control.Phase is not (PartitionMovePhase.Retired or PartitionMovePhase.Aborted)
            || last.Stage != expectedStage || actual.Control is null
            || !NativeSerialization.Serialize(actual.Control).AsSpan().SequenceEqual(NativeSerialization.Serialize(control))
            || !NativeSerialization.Serialize(last.OriginalResult).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.ObservedOriginalResult)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        RequireNoUnsettledMoveGrants(transaction, control);
        RequireMoveParentInterruptedTerminal(transaction, principal, header, control);
        var result = new PartitionMoveResult(header.MoveId, header.Partition, control.Phase,
            header.OriginalSourceOwner!, header.DestinationOwner, control.SourceCut, control.InstalledReceipt,
            control.PublishedPlacement);
        return SaveMoveParentTerminal(transaction, principal, header, result, receipt, header.InterruptedOriginalPhaseCommandId);
    }

    private PartitionMoveParentHeader SaveMoveParentTerminal(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveParentHeader header, PartitionMoveResult result,
        PartitionMoveJournalReceipt receipt, Guid? interruptedId)
    {
        var updated = header with
        {
            Generation = checked(header.Generation + PartitionMoveProtocol.SequenceStep),
            TerminalResult = result,
            TerminalObservationReceipt = receipt,
            PendingOriginalPhaseCommandId = null,
            InterruptedOriginalPhaseCommandId = interruptedId
        };
        updated = MeasureMoveParentHeader(updated,
            checked(header.RetainedMetadataBytes - NativeSerialization.Measure(header)));
        if (updated.RetainedMetadataBytes > movementCheckpoints.MaxRetainedMetadataBytesPerMove)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.DatabaseTerminalCount(header.Partition),
            PartitionMoveProtocol.SequenceStep, movementCheckpoints.MaxRetainedTerminalMovesPerDatabase);
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.DatabaseTerminalBytes(header.Partition),
            updated.RetainedMetadataBytes, movementCheckpoints.MaxRetainedTerminalBytesPerDatabase);
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.PrincipalActive(principal.Id),
            -PartitionMoveProtocol.SequenceStep, movementCheckpoints.MaxActiveMovesPerPrincipal);
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.DatabaseActive(header.Partition),
            -PartitionMoveProtocol.SequenceStep, movementCheckpoints.MaxActiveMovesPerDatabase);
        transaction.Delete(PartitionMoveParentKeys.Active(header.Partition));
        PartitionMoveParentStorage.WriteHeader(transaction, updated, Limits.MaxBatchBytes);
        return updated;
    }
}
