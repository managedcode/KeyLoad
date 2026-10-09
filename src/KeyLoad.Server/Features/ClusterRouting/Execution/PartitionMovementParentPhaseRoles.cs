using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentPhaseRoles
{

    internal static PartitionMovementParentPhaseRole RequireLastRole(PartitionMoveParentHeader header,
        PartitionMoveParentPhase last)
    {
        var role = last.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare => PartitionMovementParentPhaseRole.Prepare,
            PartitionMovePeerStage.ControlAuthorize => GrantRole(PartitionMovementParentAuthority.RequireObserved(last, last.Stage).Grant
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)),
            PartitionMovePeerStage.ControlAcknowledge => AcknowledgeRole(PartitionMovementParentAuthority.RequireObserved(last, last.Stage).Grant
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)),
            PartitionMovePeerStage.Fence => PartitionMovementParentPhaseRole.Fence,
            PartitionMovePeerStage.ControlAcceptFence => PartitionMovementParentPhaseRole.AcceptFence,
            PartitionMovePeerStage.Capture => PartitionMovementParentPhaseRole.Capture,
            PartitionMovePeerStage.ControlAdvance => AdvanceRole(last),
            PartitionMovePeerStage.StagePage => PartitionMovementParentPhaseRole.StagePage,
            PartitionMovePeerStage.Install => PartitionMovementParentPhaseRole.Install,
            PartitionMovePeerStage.ControlFinalize => PartitionMovementParentPhaseRole.FinalizePublication,
            PartitionMovePeerStage.PublishWitness => PartitionMovementParentPhaseRole.Publish,
            PartitionMovePeerStage.Retire => PartitionMovementParentPhaseRole.Retire,
            PartitionMovePeerStage.ControlCompleteRetirement => PartitionMovementParentPhaseRole.CompleteRetirement,
            PartitionMovePeerStage.ControlBeginAbort => PartitionMovementParentPhaseRole.BeginAbort,
            PartitionMovePeerStage.SourceBeginAbort => PartitionMovementParentPhaseRole.SourceClosure,
            PartitionMovePeerStage.Abort => last.OriginalGrant?.CleanupRole == PartitionMoveCleanupRole.Target
                ? PartitionMovementParentPhaseRole.TargetAbort : PartitionMovementParentPhaseRole.SourceAbort,
            PartitionMovePeerStage.ControlCancelGrants => PartitionMovementParentPhaseRole.CancelGrants,
            PartitionMovePeerStage.ControlFinalizeAbort => PartitionMovementParentPhaseRole.FinalizeAbort,
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
        };
        var cleanup = role is PartitionMovementParentPhaseRole.RetireGrant
            or PartitionMovementParentPhaseRole.Retire or PartitionMovementParentPhaseRole.RetireAcknowledge
            ? last.CleanupGeneration : PartitionMoveProtocol.EmptyCount;
        if (PartitionMovementParentPhaseIds.For(header.OriginalTransferRequest,
            header.OperatorPrincipalId, role, last.PageOrdinal, cleanup) != last.OriginalPhaseCommandId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return role;
    }

    private static PartitionMovementParentPhaseRole AdvanceRole(PartitionMoveParentPhase last)
    {
        var phase = last.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var advance = NativeSerialization.Deserialize<PartitionMoveAdvanceBody>(phase.Body.Span);
        return advance.InstalledReceipt is null ? PartitionMovementParentPhaseRole.AdvanceCaptured
            : PartitionMovementParentPhaseRole.AdvanceInstalled;
    }

    private static PartitionMovementParentPhaseRole GrantRole(PartitionMovePhaseGrant grant)
        => grant.Stage switch
        {
            PartitionMovePeerStage.Fence => PartitionMovementParentPhaseRole.FenceGrant,
            PartitionMovePeerStage.Capture => PartitionMovementParentPhaseRole.CaptureGrant,
            PartitionMovePeerStage.StagePage => PartitionMovementParentPhaseRole.StageGrant,
            PartitionMovePeerStage.Install => PartitionMovementParentPhaseRole.InstallGrant,
            PartitionMovePeerStage.PublishWitness => PartitionMovementParentPhaseRole.PublishGrant,
            PartitionMovePeerStage.Retire => PartitionMovementParentPhaseRole.RetireGrant,
            PartitionMovePeerStage.SourceBeginAbort => PartitionMovementParentPhaseRole.SourceClosureGrant,
            PartitionMovePeerStage.Abort when grant.CleanupRole == PartitionMoveCleanupRole.Target
                => PartitionMovementParentPhaseRole.TargetAbortGrant,
            PartitionMovePeerStage.Abort when grant.CleanupRole == PartitionMoveCleanupRole.Source
                => PartitionMovementParentPhaseRole.SourceAbortGrant,
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
        };

    private static PartitionMovementParentPhaseRole AcknowledgeRole(PartitionMovePhaseGrant grant)
        => grant.Stage switch
        {
            PartitionMovePeerStage.Fence => PartitionMovementParentPhaseRole.FenceAcknowledge,
            PartitionMovePeerStage.Capture => PartitionMovementParentPhaseRole.CaptureAcknowledge,
            PartitionMovePeerStage.StagePage => PartitionMovementParentPhaseRole.StageAcknowledge,
            PartitionMovePeerStage.Install => PartitionMovementParentPhaseRole.InstallAcknowledge,
            PartitionMovePeerStage.PublishWitness => PartitionMovementParentPhaseRole.PublishAcknowledge,
            PartitionMovePeerStage.Retire => PartitionMovementParentPhaseRole.RetireAcknowledge,
            PartitionMovePeerStage.SourceBeginAbort => PartitionMovementParentPhaseRole.SourceClosureAcknowledge,
            PartitionMovePeerStage.Abort when grant.CleanupRole == PartitionMoveCleanupRole.Target
                => PartitionMovementParentPhaseRole.TargetAbortAcknowledge,
            PartitionMovePeerStage.Abort when grant.CleanupRole == PartitionMoveCleanupRole.Source
                => PartitionMovementParentPhaseRole.SourceAbortAcknowledge,
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
        };
}
