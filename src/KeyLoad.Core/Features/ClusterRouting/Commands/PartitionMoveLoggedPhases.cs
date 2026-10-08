using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecutePartitionMovePhase(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, long appliedPosition)
    {
        var phase = Payload<PartitionMovePhaseCommand>(operation);
        if (!principal.ClusterAdministrator || appliedPosition <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        if (phase.Stage is not (PartitionMovePeerStage.Abort or PartitionMovePeerStage.Retire or PartitionMovePeerStage.SourceBeginAbort)
            && !PartitionMoveGrantValidation.IsLocalControl(phase.Stage))
        { PartitionMoveCleanupStorage.RequireOpen(transaction, phase.Partition, phase.MoveId, Limits.MaxBatchBytes); }
        if (phase.Stage is PartitionMovePeerStage.ControlAdmitCommand or PartitionMovePeerStage.ControlAcknowledgeCommand
            or PartitionMovePeerStage.ControlFinalizeCommand or PartitionMovePeerStage.ControlApplyCommand)
        { return Result(ExecuteControlledDocumentPhase(transaction, principal, operation, phase, appliedPosition)); }
        return phase.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare => Result(ExecuteMovePreparation(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.Fence => Result(ExecuteMoveFence(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlAcceptFence => Result(ExecuteMoveAcceptFence(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.Capture => Result(ExecuteMoveCaptureSettlement(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.StagePage => Result(ExecuteMovePage(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.Install => Result(ExecuteMoveInstallPage(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.PublishWitness => Result(ExecuteMovePublication(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlAdvance => Result(ExecuteMoveAdvance(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlFinalize => Result(ExecuteMoveFinalize(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlAuthorize => Result(ExecuteMoveAuthorize(transaction,
                principal, operation.Id, phase, operation.EvaluatedAt, appliedPosition)),
            PartitionMovePeerStage.ControlAcknowledge => Result(ExecuteMoveAcknowledge(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.SourceBeginAbort => Result(ExecuteMoveSourceBeginAbort(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlCancelGrants => Result(ExecuteMoveCancelGrants(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlBeginAbort => Result(ExecuteMoveBeginAbort(transaction,
                principal, operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.Abort or PartitionMovePeerStage.Retire => Result(ExecuteMoveCleanup(transaction,
                operation.Id, phase, appliedPosition)),
            PartitionMovePeerStage.ControlFinalizeAbort or PartitionMovePeerStage.ControlCompleteRetirement
                => Result(ExecuteMoveCompleteCleanup(transaction, principal, operation.Id, phase, appliedPosition)),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.Invalid)
        };
    }

    private PartitionMovePhaseResult ExecuteMovePreparation(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMovePrepareBody>(phase.Body.Span);
        if (body.OperatorPrincipalId != principal.Id || body.Request.MoveId != phase.MoveId
            || body.Request.Partition != phase.Partition
            || phase.ControlIntentDigest != Convert.ToHexStringLower(SHA256.HashData(phase.Body.Span)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var record = PreparePartitionMove(transaction, principal, body.Request, position);
        RequireMovePhaseIdentity(phase, record);
        return new(record.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            PartitionMoveIntentIdentity.Digest(record)), record, null, null, null);
    }

    private static void RequireMovePhaseIdentity(PartitionMovePhaseCommand phase,
        PartitionMoveControlRecord record)
    {
        PartitionMoveControlValidation.Require(record, phase.Partition);
        if (phase.MoveId != record.MoveId
            || !PartitionMoveControlValidation.SameSource(phase.SourcePlacement, record.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(phase.DestinationOwner, record.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private static PartitionMoveJournalReceipt MoveJournalReceipt(IKeyValueView view,
        Guid commandId, long position, string digest)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return new(commandId, catalog.DefaultShard, position, digest);
    }
}
