using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMovePage(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMovePageBody>(phase.Body.Span);
        RequireMovePhaseIdentity(phase, body.Control);
        PartitionMoveSourceFenceValidation.Require(body.Fence, phase.Partition);
        PartitionMoveDescriptorValidation.Require(body.Descriptor, body.Fence, Limits);
        PartitionMovePageAdmission.Require(body.Page, body.Fence, Limits, Limits.MaxBatchBytes);
        if (phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(body.Control)
            || body.Fence.ControlIntentDigest != phase.ControlIntentDigest
            || phase.PageOrdinal != body.Page.Ordinal)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var stage = RequireTargetStage(transaction, body);
        var key = PartitionMoveTargetStorage.PageKey(phase.Partition, phase.MoveId, body.Page.Ordinal);
        var previous = PartitionMoveTargetStorage.Read<PartitionMoveImagePage>(transaction, key, Limits.MaxBatchBytes);
        if (previous is not null)
        {
            PartitionMovePageAdmission.Require(previous, body.Fence, Limits, Limits.MaxBatchBytes);
            if (previous.Family != body.Page.Family || previous.Digest != body.Page.Digest
                || previous.MoveId != body.Page.MoveId || previous.Partition != body.Page.Partition)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId,
                position, phase.ControlIntentDigest), body.Control, body.Fence, null, null);
        }
        if (stage.Installed || stage.Published || stage.AcceptedPages != body.Page.Ordinal)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var encodedBytes = NativeSerialization.Measure(body.Page);
        if (encodedBytes > Limits.MaxQueryReadBytes - stage.RetainedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveTargetStorage.Write(transaction, key, body.Page, Limits.MaxBatchBytes);
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveTargetStorage.Key(phase.Partition),
            stage with
            {
                AcceptedPages = checked(stage.AcceptedPages + PartitionMoveProtocol.SequenceStep),
                RetainedBytes = stage.RetainedBytes + encodedBytes
            }, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId,
            position, phase.ControlIntentDigest), body.Control, body.Fence, null, null);
    }

    private PartitionMoveTargetStage RequireTargetStage(IAtomicTransaction transaction,
        PartitionMovePageBody body)
    {
        var key = PartitionMoveTargetStorage.Key(body.Control.Partition);
        var existing = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(transaction, key, Limits.MaxBatchBytes);
        if (existing is not null)
        {
            if (existing.Control.MoveId != body.Control.MoveId
                || existing.Descriptor.Digest != body.Descriptor.Digest
                || existing.Fence.SourceCut != body.Fence.SourceCut
                || existing.Fence.ControlIntentDigest != body.Fence.ControlIntentDigest
                || !PhysicalOwnerEntryValidation.SameOwner(existing.Fence.ControlOwner, body.Fence.ControlOwner)
                || !PartitionMoveControlValidation.SameSource(existing.Fence.SourcePlacement, body.Fence.SourcePlacement))
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return existing;
        }
        foreach (var family in PartitionRecordFamilies.All)
        {
            if (!PartitionMoveFamilyCapture.ControlOwned(family)
                && transaction.Scan(KeySpace.Partition(family, body.Control.Partition),
                    PartitionMoveProtocol.SequenceStep).Records.Length != PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        }
        return new(PartitionMoveProtocol.Version, body.Control, body.Fence, body.Descriptor,
            PartitionMoveProtocol.EmptyCount, PartitionMoveProtocol.EmptyCount, false, false);
    }
}
