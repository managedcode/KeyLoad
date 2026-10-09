using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader CancelUnpreparedMoveParent(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header,
        PartitionMovePhaseCommand phase, PartitionMoveJournalReceipt receipt)
    {
        var pending = RequireUnpreparedMoveParentCancellation(transaction, principal, body, header);
        var original = pending.OriginalPhaseCommandId;
        var cancellation = new PartitionMoveParentCancellation(PartitionMoveProtocol.Version,
            original, phase, receipt, principal.PolicyEpoch);
        return SaveMoveParentPhaseObservation(transaction, header, pending,
            pending with { Cancellation = cancellation }, clearPending: false);
    }

    private PartitionMoveParentPhase RequireUnpreparedMoveParentCancellation(IKeyValueView transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header)
    {
        var original = RequireMoveParentCancellationShape(body, header);
        var pending = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            original, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentCancellationOriginal(body, pending);
        if (pending.Cancellation is not null
            || PartitionMoveControlStorage.ReadHistory(transaction, header.Partition, header.MoveId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var selected = CommandOutcomeKeyResolver.Select(transaction, principal.Id, original,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, header.Partition));
        if (selected.Outcome is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var directory = RequireMoveDirectory(transaction);
        var placement = ResolveRegisteredPlacement(transaction, header.Partition, directory.ControlOwner);
        if (!NativeSerialization.Serialize(placement).AsSpan().SequenceEqual(NativeSerialization.Serialize(header.SourcePlacement)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var active = transaction.ReadOwnedValue(PartitionMoveParentKeys.Active(header.Partition));
        if (active is null || NativeSerialization.Deserialize<Guid>(active) != header.MoveId)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        return pending;
    }

    private PartitionMoveParentHeader ObserveMoveParentCancellation(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header,
        PartitionMoveJournalReceipt receipt)
    {
        var original = RequireMoveParentCancellationShape(body, header);
        RequireObservedMoveParentCancellation(transaction, principal, body, header);
        var result = new PartitionMoveResult(header.MoveId, header.Partition, PartitionMovePhase.Aborted,
            header.OriginalSourceOwner!, header.DestinationOwner, PartitionMoveProtocol.EmptyCount, null, null);
        return SaveMoveParentTerminal(transaction, principal, header, result, receipt, original);
    }

    private void RequireObservedMoveParentCancellation(IKeyValueView transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header)
    {
        var original = RequireMoveParentCancellationShape(body, header);
        var pending = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            original, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentCancellationOriginal(body, pending);
        var cancellation = pending.Cancellation
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentCancellationBinding(header, pending, cancellation);
        if (PartitionMoveControlStorage.ReadHistory(transaction, header.Partition, header.MoveId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var actual = ReadMoveParentCancellationOutcome(transaction, principal, header, pending)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (body.ObservedOriginalResult is null
            || !NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.ObservedOriginalResult)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
    }

    private OperationResult? ReadMoveParentCancellationOutcome(IKeyValueView transaction,
        PrincipalRecord principal, PartitionMoveParentHeader header, PartitionMoveParentPhase pending)
    {
        var cancellation = pending.Cancellation;
        if (cancellation is null)
        { return null; }
        RequireMoveParentCancellationBinding(header, pending, cancellation);
        RequireMoveParentOriginalIssuance(transaction, principal, pending);
        var stored = CommandOutcomeKeyResolver.Select(transaction, principal.Id, cancellation.CancellationReceipt.CommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, header.Partition)).Outcome;
        if (stored is null)
        { return null; }
        var identity = MovementPhaseIdentityJson(cancellation.CancellationPhase);
        var fingerprint = CommandFingerprint(new(cancellation.CancellationReceipt.CommandId,
            OperationKind.PartitionMovementPhase, principal.Id, default, identity));
        if (stored.Incarnation != Store.Identity.Incarnation || stored.PolicyEpoch != cancellation.CancellationPolicyEpoch
            || stored.Fingerprint != fingerprint
            || stored.Result.Error is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var actual = stored.Result.Get<PartitionMovePhaseResult>();
        if (actual.MoveId != header.MoveId || actual.Stage != PartitionMovePeerStage.ControlCheckpoint
            || actual.Control is not null
            || !NativeSerialization.Serialize(actual.Journal).AsSpan().SequenceEqual(NativeSerialization.Serialize(cancellation.CancellationReceipt)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        return stored.Result;
    }

    private static Guid RequireMoveParentCancellationShape(PartitionMoveCheckpointBody body, PartitionMoveParentHeader header)
    {
        if (body.Action is not (PartitionMoveCheckpointAction.CancelUnprepared or PartitionMoveCheckpointAction.ObserveCancellation)
            || body.OriginalTransferRequest.Mode != PartitionMoveMode.Abort || header.RetainedPhaseCount != PartitionMoveProtocol.SequenceStep
            || header.InterruptedOriginalPhaseCommandId is not null || header.PendingOriginalPhaseCommandId is not { } original
            || body.OriginalPhaseCommandId != original || header.LastOriginalPhaseCommandId != original
            || body.OriginalAuthorization is not null || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.NextOriginalPhase is not null || body.NextOriginalPhaseCommandId is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || (body.Action == PartitionMoveCheckpointAction.CancelUnprepared) != (body.ObservedOriginalResult is null))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        return original;
    }

    private static void RequireMoveParentCancellationOriginal(PartitionMoveCheckpointBody body, PartitionMoveParentPhase pending)
    {
        if (pending.Stage != PartitionMovePeerStage.ControlPrepare || pending.OriginalPhase is null
            || pending.OriginalResult is not null || pending.OriginalGrant is not null || pending.OriginalAuthorization is not null
            || pending.ObservationCheckpointReceipt is not null || body.OriginalPhase is null
            || body.OriginalExpiresAt != pending.OriginalExpiresAt || body.OriginalRequestNonce != pending.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != pending.OriginalCaptureReleaseNonce
            || !NativeSerialization.Serialize(body.OriginalPhase).AsSpan().SequenceEqual(NativeSerialization.Serialize(pending.OriginalPhase)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
    }
}
