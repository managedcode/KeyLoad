using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static partial class PartitionMovementParentNativePhases
{
    internal static PartitionMovePhaseCommand Capture(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveCaptureRequest request)
    {
        if (request.OperatorPrincipalId != header.OperatorPrincipalId || request.Fence.MoveId != header.MoveId
            || request.Fence.Partition != header.Partition || request.Fence.SourceCut != control.SourceCut)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return Frame(header, control, PartitionMovePeerStage.Capture, NativeSerialization.Serialize(request));
    }

    internal static PartitionMovePhaseCommand Advance(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveParentPhase captured, PartitionMoveParentPhase? installed)
    {
        var descriptor = captured.OriginalDescriptor
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var fence = captured.OriginalFence
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        _ = Observed(captured, PartitionMovePeerStage.Capture);
        CommitReceipt? receipt = null;
        Guid? installGrantId = null;
        if (installed is not null)
        {
            receipt = Observed(installed, PartitionMovePeerStage.Install).InstalledReceipt
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            installGrantId = installed.OriginalGrant?.GrantId
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
        var sourceGrantId = captured.OriginalGrant?.GrantId
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        return Frame(header, control, PartitionMovePeerStage.ControlAdvance,
            NativeSerialization.Serialize(new PartitionMoveAdvanceBody(header.OperatorPrincipalId, control,
                fence, descriptor, receipt, sourceGrantId, installGrantId)));
    }

    internal static PartitionMovePhaseCommand FinalizePublication(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control)
        => Frame(header, control, PartitionMovePeerStage.ControlFinalize,
            NativeSerialization.Serialize(new PartitionMoveControlBody(header.OperatorPrincipalId, control)));

    internal static PartitionMovePhaseCommand Publish(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveParentPhase finalized,
        ImmutableArray<ResourceDefinition> originalResources)
    {
        var actual = Observed(finalized, PartitionMovePeerStage.ControlFinalize);
        var row = actual.PublishedPlacement
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var token = control.InstalledReceipt
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var publication = new PartitionMovePublishedPlacement(PartitionMoveProtocol.Version,
            header.MoveId, row, header.SourcePlacement, header.DestinationOwner, token, actual.Journal);
        return Frame(header, control, PartitionMovePeerStage.PublishWitness,
            NativeSerialization.Serialize(new PartitionMovePublishBody(header.OperatorPrincipalId, control,
                publication, originalResources)));
    }

    internal static PartitionMovePhaseCommand Cleanup(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMovePeerStage stage, PartitionMoveCleanupRole role,
        int family, int batch, Guid precedingGrantId, PartitionMovePublishedPlacement? publication)
    {
        if (stage is not (PartitionMovePeerStage.Retire or PartitionMovePeerStage.Abort or PartitionMovePeerStage.SourceBeginAbort)
            || !Enum.IsDefined(role) || family < PartitionMovementProtocol.InitialCleanupFamily || family > PartitionMoveCleanupFamilies.All.Length || batch < PartitionMovementProtocol.InitialCleanupBatch
            || role == PartitionMoveCleanupRole.Source && precedingGrantId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        return Frame(header, control, stage, NativeSerialization.Serialize(new PartitionMoveCleanupBody(
            header.OperatorPrincipalId, control, role, family, precedingGrantId, publication)), batch);
    }

    internal static PartitionMovePhaseCommand Complete(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveParentPhase source, PartitionMoveParentPhase? target,
        PartitionMovePeerStage stage, int ordinal = PartitionMovementProtocol.InitialPhaseOrdinal)
    {
        if (stage is not (PartitionMovePeerStage.ControlCompleteRetirement
            or PartitionMovePeerStage.ControlCancelGrants or PartitionMovePeerStage.ControlFinalizeAbort))
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var abort = stage != PartitionMovePeerStage.ControlCompleteRetirement;
        var sourceProof = TerminalCleanup(source, abort ? PartitionMovePeerStage.Abort : PartitionMovePeerStage.Retire);
        var targetProof = target is null ? null : TerminalCleanup(target, PartitionMovePeerStage.Abort);
        if (abort != (targetProof is not null))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var body = new PartitionMoveCompletionBody(header.OperatorPrincipalId, control,
            sourceProof.Journal, targetProof?.Journal, source.OriginalGrant!.GrantId, target?.OriginalGrant?.GrantId,
            source.OriginalPhase!.Body, target?.OriginalPhase?.Body ?? ReadOnlyMemory<byte>.Empty);
        return Frame(header, control, stage, NativeSerialization.Serialize(body), ordinal);
    }

    private static PartitionMovePhaseResult TerminalCleanup(PartitionMoveParentPhase original,
        PartitionMovePeerStage expected)
    {
        var actual = Observed(original, expected);
        if (original.OriginalPhase is null || original.OriginalGrant is null || actual.Cleanup?.Completion is null
            || !NativeSerialization.Serialize(actual.Cleanup.Completion).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(actual.Journal)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return actual;
    }

    private static PartitionMovePhaseResult Observed(PartitionMoveParentPhase original, PartitionMovePeerStage expected)
    {
        if (original.Stage != expected || original.OriginalResult is null
            || original.ObservationCheckpointReceipt is null || original.OriginalResult.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var actual = original.OriginalResult.Get<PartitionMovePhaseResult>();
        if (actual.Stage != expected || actual.MoveId != original.MoveId
            || actual.Journal.CommandId != original.OriginalPhaseCommandId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return actual;
    }
}
