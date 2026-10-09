using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveParentHeader? ReadPartitionMovementParentHeader(string principalId,
        PartitionMoveRequest request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidatePartition(request.Partition);
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view);
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
            return header;
        });
        work.CheckResult(result);
        return result;
    }

    internal PartitionMoveParentPhase ReadPartitionMovementParentPhase(string principalId,
        PartitionMoveRequest request, Guid originalPhaseId, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidatePartition(request.Partition);
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var directory = RequireMoveDirectory(charged);
            var header = PartitionMoveParentStorage.Header(charged, request.Partition, request.MoveId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            if (header.OperatorPrincipalId != principal.Id)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            PartitionMoveParentValidation.RequireReplayScope(header, principal.Id, request);
            if (!PhysicalOwnerEntryValidation.SameOwner(header.ControlOwner, directory.ControlOwner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return PartitionMoveParentStorage.Phase(charged, request.Partition, request.MoveId,
                originalPhaseId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        });
        work.CheckResult(result);
        return result;
    }
}
