using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveTransferReadAuthority ReadPartitionMovementTransferAuthority(string principalId,
        PartitionMoveRequest request, Guid capturePhaseId, ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidatePartition(request.Partition);
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var header = PartitionMoveParentStorage.Header(charged, request.Partition, request.MoveId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            PartitionMoveParentValidation.RequireReplayScope(header, principal.Id, request);
            if (!PhysicalOwnerEntryValidation.SameOwner(header.ControlOwner, RequireMoveDirectory(charged).ControlOwner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            if (header.OriginalCapturePhaseCommandId is not { } originalCaptureId
                || originalCaptureId != capturePhaseId)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            var capture = PartitionMoveParentStorage.Phase(charged, request.Partition, request.MoveId,
                capturePhaseId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var control = PartitionMoveControlStorage.ReadHistory(charged, request.Partition, request.MoveId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var applied = charged.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            var authority = new PartitionMoveTransferReadAuthority(header, capture, control, NativeSerialization.Deserialize<long>(applied));
            PartitionMoveTransferAuthorityValidation.Require(authority, Limits);
            return authority;
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
