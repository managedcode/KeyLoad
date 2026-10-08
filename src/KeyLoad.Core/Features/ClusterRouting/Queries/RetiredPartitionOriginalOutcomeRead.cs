using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long UncommittedReceiptPosition = 0;

    private readonly record struct RetiredOriginalOutcomeReadScope(CommandRequest Command,
        PartitionMoveSourceFenceRecord Fence, PartitionMoveControlRecord Control,
        AtomicPartitionPlacementResolution Placement, PhysicalShardRecord ControlOwner, PhysicalShardRecord LocalOwner);

    private RetiredOriginalOutcomeReadScope? CaptureRetiredOriginalOutcomeReadScope(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.Batch || configuredPhysicalOwner is null)
        { return null; }
        var command = Payload<CommandRequest>(operation);
        ClusterPrincipalPolicy.RequireOperation(principal, operation.Kind);
        RequireBatchMutationCapabilities(principal, command);
        var fence = PartitionMoveSourceFenceStorage.Read(view, command.Partition, Limits.MaxBatchBytes);
        if (fence is null)
        { return null; }
        var control = PartitionMoveControlStorage.ReadHistory(view, command.Partition, fence.MoveId, Limits.MaxBatchBytes);
        if (control?.Phase != PartitionMovePhase.Retired)
        { return null; }
        var directory = RequireMoveDirectory(view);
        var placement = ReadPlacementWitness(view, command.Partition);
        RequireRetiredOriginalPlacement(fence, control, placement, directory.ControlOwner, configuredPhysicalOwner);
        if (command.OwnershipEpoch != fence.SourcePlacement.PlacementEpoch)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        AuthorizeBatchMutations(view, principal, command, false, BatchResourceAdmission.RetiredOutcomeMetadata);
        return new(command, fence, control, placement, directory.ControlOwner, configuredPhysicalOwner);
    }

    private static void RequireRetiredOriginalOutcomeIdentity(CommandRequest command, Guid commandId,
        CommitReceipt receipt, PartitionMoveSourceFenceRecord fence, PartitionMoveControlRecord control,
        AtomicPartitionPlacementResolution placement, PhysicalShardRecord controlOwner, PhysicalShardRecord localOwner)
    {
        RequireRetiredOriginalPlacement(fence, control, placement, controlOwner, localOwner);
        if (command.CommandId != commandId || command.Partition != fence.Partition
            || command.OwnershipEpoch != fence.SourcePlacement.PlacementEpoch
            || receipt.CommandId != commandId || receipt.Token.Incarnation != localOwner.Incarnation
            || receipt.Token.AtomicPartitionId != fence.Partition.AtomicPartitionId
            || receipt.Token.OwnershipEpoch != fence.SourcePlacement.PlacementEpoch
            || receipt.Token.Position <= UncommittedReceiptPosition || receipt.Token.Position > fence.SourceCut)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
    private static void RequireRetiredOriginalPlacement(PartitionMoveSourceFenceRecord fence,
        PartitionMoveControlRecord control, AtomicPartitionPlacementResolution placement,
        PhysicalShardRecord controlOwner, PhysicalShardRecord localOwner)
    {
        if (!PhysicalOwnerEntryValidation.SameOwner(fence.ControlOwner, controlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(controlOwner, localOwner)
            || fence.SourcePlacement.PhysicalShardId != localOwner.PhysicalShardId
            || fence.SourcePlacement.Incarnation != localOwner.Incarnation
            || !PartitionMoveControlValidation.SameSource(fence.SourcePlacement, control.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.DestinationOwner, control.DestinationOwner)
            || fence.SourceCut != control.SourceCut || fence.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(control)
            || control.PublishedPlacement is null || placement.PhysicalShardId != control.DestinationOwner.PhysicalShardId
            || placement.Incarnation != control.DestinationOwner.Incarnation
            || placement.PlacementEpoch != control.PublishedPlacement.PlacementEpoch
            || placement.Revision != control.PublishedPlacement.Revision)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

}
