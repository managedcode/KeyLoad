using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveInstallPage(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveInstallBody>(phase.Body.Span);
        RequireMovePhaseIdentity(phase, body.Control);
        PartitionMoveDescriptorValidation.Require(body.Descriptor, body.Fence, Limits);
        var stage = RequireMoveInstallStage(transaction, body);
        RequireInstallStage(stage, body, phase);
        var totalPages = body.Descriptor.Families.Sum(value => value.PageCount);
        if (phase.PageOrdinal == totalPages)
        { return CompleteMoveInstall(transaction, commandId, phase, position, body, stage, totalPages); }
        if (phase.PageOrdinal != stage.InstalledPages || stage.Installed || stage.Published
            || phase.PageOrdinal >= stage.AcceptedPages)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var page = PartitionMoveTargetStorage.Read<PartitionMoveImagePage>(transaction,
            PartitionMoveTargetStorage.PageKey(phase.Partition, phase.MoveId, phase.PageOrdinal),
            Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage);
        PartitionMovePageAdmission.Require(page, body.Fence, Limits, Limits.MaxBatchBytes);
        InstallMoveRecords(transaction, page, body.Descriptor, body.Fence.SourcePlacement.Incarnation);
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveTargetStorage.Key(phase.Partition),
            stage with { InstalledPages = checked(stage.InstalledPages + PartitionMoveProtocol.SequenceStep) },
            Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), body.Control, body.Fence, null, null);
    }

    private void InstallMoveRecords(IAtomicTransaction transaction, PartitionMoveImagePage page,
        PartitionMoveImageDescriptor descriptor, Guid sourceIncarnation)
    {
        foreach (var record in page.Records)
        {
            if (transaction.ReadOwnedValue(record.Key.ToArray()) is not null)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            PartitionMoveBlobImport.Charge(transaction, page, record, descriptor, Store.Identity.Incarnation, Limits);
            transaction.Put(record.Key.ToArray(), PartitionMoveBlobImport.Value(page, record,
                sourceIncarnation, Store.Identity.Incarnation));
        }
    }

    private static void RequireInstallStage(PartitionMoveTargetStage stage,
        PartitionMoveInstallBody body, PartitionMovePhaseCommand phase)
    {
        if (stage.Control.MoveId != phase.MoveId || stage.Descriptor.Digest != body.Descriptor.Digest
            || stage.Fence.SourceCut != body.Fence.SourceCut
            || stage.Fence.ControlIntentDigest != phase.ControlIntentDigest
            || !PartitionMoveControlValidation.SameSource(stage.Fence.SourcePlacement, body.Fence.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(stage.Fence.ControlOwner, body.Fence.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(stage.Fence.DestinationOwner, body.Fence.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
