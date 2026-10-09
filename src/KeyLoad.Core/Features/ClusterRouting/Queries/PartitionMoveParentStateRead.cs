using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveParentState ReadPartitionMovementParentState(string principalId, PartitionMoveRequest request,
        Guid selectedId, ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
    {
        ValidatePartition(request.Partition);
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var directory = RequireMoveDirectory(charged);
            var header = PartitionMoveParentStorage.Header(charged, request.Partition, request.MoveId, Limits.MaxBatchBytes);
            if (header is not null)
            {
                if (header.OperatorPrincipalId != principal.Id)
                { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
                PartitionMoveParentValidation.RequireReplayScope(header, principal.Id, request);
                if (!PhysicalOwnerEntryValidation.SameOwner(header.ControlOwner, directory.ControlOwner))
                { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            }
            var pending = ReadParentStatePhase(charged, request, header?.PendingOriginalPhaseCommandId ?? Guid.Empty);
            var last = ReadSharedParentStatePhase(charged, request, header?.LastOriginalPhaseCommandId ?? Guid.Empty, pending);
            var selected = ReadSharedParentStatePhase(charged, request, selectedId, pending, last);
            var interrupted = ReadSharedParentStatePhase(charged, request, header?.InterruptedOriginalPhaseCommandId ?? Guid.Empty, pending, last, selected);
            var cancellationPhase = pending ?? interrupted;
            var cancellationOutcome = header is not null && cancellationPhase is not null
                ? ReadMoveParentCancellationOutcome(charged, principal, header, cancellationPhase) : null;
            var selectedOutcome = header is not null && selected is { OriginalPhase: not null }
                && PartitionMoveGrantValidation.IsLocalControl(selected.Stage)
                ? ReadCheckpointLocalOutcome(charged, principal, selected) : null;
            var control = PartitionMoveControlStorage.ReadHistory(charged, request.Partition, request.MoveId, Limits.MaxBatchBytes);
            if (header is { CleanupGeneration: > PartitionMoveProtocol.EmptyCount })
            {
                RequireClosedRetireGrantIntegrity(charged, control
                    ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority));
            }
            var placement = ResolveRegisteredPlacement(charged, request.Partition, directory.ControlOwner);
            var applied = charged.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            return new PartitionMoveParentState(header, pending, last, selected, control, directory,
                placement, NativeSerialization.Deserialize<long>(applied), interrupted, cancellationOutcome, selectedOutcome,
                principal.PolicyEpoch, principal.Id);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private PartitionMoveParentPhase? ReadSharedParentStatePhase(IKeyValueView view, PartitionMoveRequest request,
        Guid id, PartitionMoveParentPhase? first, PartitionMoveParentPhase? second = null, PartitionMoveParentPhase? third = null)
    {
        if (id == Guid.Empty)
        { return null; }
        if (first?.OriginalPhaseCommandId == id)
        { return first; }
        if (second?.OriginalPhaseCommandId == id)
        { return second; }
        if (third?.OriginalPhaseCommandId == id)
        { return third; }
        return ReadParentStatePhase(view, request, id);
    }

    private PartitionMoveParentPhase? ReadParentStatePhase(IKeyValueView view, PartitionMoveRequest request, Guid id)
    {
        if (id == Guid.Empty)
        { return null; }
        return PartitionMoveParentStorage.Phase(view, request.Partition, request.MoveId, id, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
    }
}
