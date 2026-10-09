using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveTransferAuthorityValidation
{
    private const int UnappliedPosition = 0;
    internal static void Require(PartitionMoveTransferReadAuthority authority, DatabaseLimits limits)
    {
        var header = authority.Header;
        var capture = authority.CapturePhase;
        var control = authority.CurrentControl;
        PartitionMoveParentValidation.RequireHeader(header, header.Partition, header.MoveId);
        PartitionMoveParentValidation.RequirePhase(capture, header.Partition, header.MoveId, capture.OriginalPhaseCommandId);
        if (header.OriginalCapturePhaseCommandId != capture.OriginalPhaseCommandId
            || header.TerminalResult is not null || capture.Stage != PartitionMovePeerStage.Capture
            || capture.OriginalPhase is null || capture.OriginalGrant is null
            || capture.OriginalResult is null || capture.OriginalResult.Error is not null
            || capture.ObservationCheckpointReceipt is null || capture.CaptureProofCheckpointReceipt is null
            || capture.OriginalCaptureWitness is null || capture.OriginalOutcomeWitness is null
            || capture.OriginalDescriptor is null || capture.OriginalFence is null
            || control.MoveId != header.MoveId || control.Partition != header.Partition
            || control.PrincipalId != header.OperatorPrincipalId || control.PolicyEpoch != header.InitialPolicyEpoch
            || control.Phase is not (PartitionMovePhase.Captured or PartitionMovePhase.Installed or PartitionMovePhase.Published)
            || authority.ControlReadCut < control.ControlPosition
            || !PartitionMoveControlValidation.SameSource(control.SourcePlacement, header.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(control.DestinationOwner, header.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(capture.OriginalReceiverOwner, header.OriginalSourceOwner!))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        RequireOriginalImage(authority, limits);
    }

    private static void RequireOriginalImage(PartitionMoveTransferReadAuthority authority, DatabaseLimits limits)
    {
        var capture = authority.CapturePhase;
        var fence = capture.OriginalFence!;
        var descriptor = capture.OriginalDescriptor!;
        PartitionMoveDescriptorValidation.Require(descriptor, fence, limits);
        var result = capture.OriginalResult!.Get<PartitionMovePhaseResult>();
        if (result.MoveId != capture.MoveId || result.Stage != capture.Stage
            || result.Journal.CommandId != capture.OriginalPhaseCommandId || result.Journal.AppliedPosition <= UnappliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(result.Journal.PhysicalOwner, capture.OriginalReceiverOwner)
            || result.Fence is null
            || !NativeSerialization.Serialize(result.Fence).AsSpan().SequenceEqual(NativeSerialization.Serialize(fence))
            || authority.CurrentControl.SourceCut != descriptor.SourceCut
            || authority.CurrentControl.ImageDigest != descriptor.Digest
            || fence.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(authority.CurrentControl)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.ControlOwner, authority.Header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.DestinationOwner, authority.Header.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
