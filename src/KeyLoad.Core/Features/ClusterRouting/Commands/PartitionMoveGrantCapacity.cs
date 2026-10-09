using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveReceiverIssueCapacity(IAtomicTransaction transaction, PrincipalRecord principal,
        Guid authorizationCommandId, PartitionMovePhaseCommand intended, PartitionMovePhaseGrant candidate)
    {
        var header = PartitionMoveParentStorage.Header(transaction, intended.Partition, intended.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (header.OperatorPrincipalId != principal.Id || header.TerminalResult is not null
            || header.PendingOriginalPhaseCommandId != authorizationCommandId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var request = header.OriginalTransferRequest;
        var pending = ReadParentStatePhase(transaction, request, authorizationCommandId)!;
        var last = ReadSharedParentStatePhase(transaction, request,
            header.LastOriginalPhaseCommandId, pending);
        var selected = ReadSharedParentStatePhase(transaction, request,
            header.OriginalCapturePhaseCommandId ?? Guid.Empty, pending, last);
        var interrupted = ReadSharedParentStatePhase(transaction, request,
            header.InterruptedOriginalPhaseCommandId ?? Guid.Empty, pending, last, selected);
        var cancellation = pending;
        var cancellationOutcome = cancellation is null ? null
            : ReadMoveParentCancellationOutcome(transaction, principal, header, cancellation);
        var selectedOutcome = selected is { OriginalPhase: not null }
            && PartitionMoveGrantValidation.IsLocalControl(selected.Stage)
            ? ReadCheckpointLocalOutcome(transaction, principal, selected) : null;
        var directory = RequireMoveDirectory(transaction);
        var control = PartitionMoveControlStorage.ReadHistory(transaction, request.Partition, request.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var applied = transaction.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var state = new PartitionMoveParentState(header, pending, last, selected, control, directory,
            ResolveRegisteredPlacement(transaction, request.Partition, directory.ControlOwner),
            NativeSerialization.Deserialize<long>(applied), interrupted, cancellationOutcome, selectedOutcome,
            principal.PolicyEpoch, principal.Id);
        var context = new PartitionMoveReceiverIssueCapacityContext(PartitionMoveProtocol.Version, state,
            intended, candidate, Limits.MaxBatchBytes, movementCheckpoints.MaxPhaseRecordsPerMove,
            movementCheckpoints.MaxRetainedMetadataBytesPerMove);
        if (NativeSerialization.Measure(context) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        movementCheckpointVerifier.RequireReceiverIssueCapacity(NativeSerialization.Serialize(context));
    }
}
