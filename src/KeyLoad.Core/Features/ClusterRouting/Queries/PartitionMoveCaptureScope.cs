using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal void ValidateVerifiedPartitionMovementCaptureScope(string localPrincipalId,
        PartitionMovePeerEnvelope original, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (original.Stage != PartitionMovePeerStage.Capture || original.Grant is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMovementReceiver(original);
        var body = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(original.Body.Span);
        Store.Read(view =>
        {
            var admitted = work.CreateView(view);
            var now = EvaluationClock.GetUtcNow();
            var principal = Principal(admitted, localPrincipalId, now);
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(admitted)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            PartitionMoveGrantValidation.Require(original, original.Grant.PhaseCommandId,
                catalog.DefaultShard, now);
            PartitionMoveCleanupStorage.RequireOpen(admitted, original.Partition, original.MoveId, Limits.MaxBatchBytes);
            var current = PartitionMoveSourceFenceStorage.Read(admitted, original.Partition,
                Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced);
            RequireCurrentCaptureFence(original, body.Fence, current);
            return true;
        });
        work.Check();
    }

    private static void RequireCurrentCaptureFence(PartitionMovePeerEnvelope original,
        PartitionMoveSourceFenceRecord expected, PartitionMoveSourceFenceRecord current)
    {
        PartitionMoveSourceFenceValidation.Require(expected, original.Partition);
        if (current.MoveId != original.MoveId || expected.MoveId != original.MoveId
            || current.ControlIntentDigest != original.ControlIntentDigest
            || expected.ControlIntentDigest != original.ControlIntentDigest
            || current.SourceCut != expected.SourceCut
            || !PartitionMoveControlValidation.SameSource(current.SourcePlacement, expected.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(current.ControlOwner, expected.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(current.DestinationOwner, expected.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
