using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveImage CaptureVerifiedPartitionMovement(string localPrincipalId,
        PartitionMovePeerEnvelope verified, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        PartitionMovePeerEnvelopeValidation.RequireStructure(verified, Limits.MaxBatchBytes);
        if (verified.Stage != PartitionMovePeerStage.Capture || verified.Grant is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMovementReceiver(verified);
        var body = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(verified.Body.Span);
        if (body.MaximumPageBytes <= PartitionMoveProtocol.EmptyCount
            || body.MaximumPageBytes > Limits.MaxBatchBytes
            || body.MaximumImageBytes <= PartitionMoveProtocol.EmptyCount
            || body.MaximumImageBytes > Limits.MaxQueryReadBytes
            || body.MaximumRecords <= PartitionMoveProtocol.EmptyCount
            || body.MaximumRecords > Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return Store.Read(view =>
        {
            var admitted = work.CreateView(view);
            var principal = Principal(admitted, localPrincipalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(admitted)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            PartitionMoveGrantValidation.Require(verified, verified.Grant.PhaseCommandId,
                catalog.DefaultShard, EvaluationClock.GetUtcNow());
            PartitionMoveCleanupStorage.RequireOpen(admitted, verified.Partition, verified.MoveId, Limits.MaxBatchBytes);
            if (body.Fence.ControlIntentDigest != verified.ControlIntentDigest
                || body.Fence.MoveId != verified.MoveId || body.Fence.Partition != verified.Partition)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return PartitionMoveImageCapture.Capture(view, body.Fence, work, Limits,
                body.MaximumPageBytes, body.MaximumImageBytes, body.MaximumRecords, verified.Grant.Resources);
        });
    }
}
