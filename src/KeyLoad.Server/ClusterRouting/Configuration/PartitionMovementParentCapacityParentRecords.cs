using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityParentRecords
{
    internal static long Bound(PartitionMoveParentPhase value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + GuidFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalBodyDigest) + (value.OriginalPhase is { } presentvalueOriginalPhase ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalPhase) : ReferenceFieldBytes)
            + (value.OriginalAuthorization is { } presentvalueOriginalAuthorization ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalAuthorization) : ReferenceFieldBytes) + (value.OriginalResult is { } presentvalueOriginalResult ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalResult) : ReferenceFieldBytes)
            + (value.OriginalDescriptor is { } presentvalueOriginalDescriptor ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalDescriptor) : ReferenceFieldBytes) + (value.OriginalFence is { } presentvalueOriginalFence ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalFence) : ReferenceFieldBytes)
            + PartitionMovementParentCapacityBounds.Bound(value.AdmissionCheckpointReceipt) + (value.ObservationCheckpointReceipt is { } presentvalueObservationCheckpointReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueObservationCheckpointReceipt) : ReferenceFieldBytes)
            + (value.OriginalGrant is { } presentvalueOriginalGrant ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalGrant) : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.OriginalReceiverOwner)
            + DateTimeFieldBytes + (value.OriginalCaptureWitness is { } presentvalueOriginalCaptureWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalCaptureWitness) : ReferenceFieldBytes)
            + GuidFieldBytes + (value.CaptureProofCheckpointReceipt is { } presentvalueCaptureProofCheckpointReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueCaptureProofCheckpointReceipt) : ReferenceFieldBytes)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalPhaseIdentityDigest) + (value.OriginalOutcomeWitness is { } presentvalueOriginalOutcomeWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalOutcomeWitness) : ReferenceFieldBytes)
            + GuidFieldBytes + (value.Cancellation is { } presentvalueCancellation ? PartitionMovementParentCapacityBounds.Bound(presentvalueCancellation) : ReferenceFieldBytes)
            + ScalarFieldBytes + (value.OriginalReceiverIssuanceWitness is { } presentvalueOriginalReceiverIssuanceWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverIssuanceWitness) : ReferenceFieldBytes)
            + (value.ReceiverIssuanceCheckpointReceipt is { } presentvalueReceiverIssuanceCheckpointReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverIssuanceCheckpointReceipt) : ReferenceFieldBytes) + (value.OriginalReceiverSourceWitness is { } presentvalueOriginalReceiverSourceWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverSourceWitness) : ReferenceFieldBytes)
            + (value.ReceiverSourceCheckpointReceipt is { } presentvalueReceiverSourceCheckpointReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverSourceCheckpointReceipt) : ReferenceFieldBytes) + (value.OriginalReceiverIssuePacket is { } presentvalueOriginalReceiverIssuePacket ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverIssuePacket) : ReferenceFieldBytes)
            + (value.ReceiverIssuePacketCheckpointReceipt is { } presentvalueReceiverIssuePacketCheckpointReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverIssuePacketCheckpointReceipt) : ReferenceFieldBytes) + ScalarFieldBytes
            + (value.RetireCancellation is { } presentvalueRetireCancellation ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellation) : ReferenceFieldBytes) + (value.RetireCancellationAttempt is { } presentvalueRetireCancellationAttempt ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellationAttempt) : ReferenceFieldBytes));

    internal static long Bound(PartitionMoveCheckpointBody value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OperatorPrincipalId) + PartitionMovementParentCapacityBounds.Bound(value.OriginalTransferRequest)
            + ScalarFieldBytes + GuidFieldBytes
            + (value.OriginalPhase is { } presentvalueOriginalPhase ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalPhase) : ReferenceFieldBytes) + (value.OriginalAuthorization is { } presentvalueOriginalAuthorization ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalAuthorization) : ReferenceFieldBytes)
            + (value.ObservedOriginalResult is { } presentvalueObservedOriginalResult ? PartitionMovementParentCapacityBounds.Bound(presentvalueObservedOriginalResult) : ReferenceFieldBytes) + (value.OriginalDescriptor is { } presentvalueOriginalDescriptor ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalDescriptor) : ReferenceFieldBytes)
            + (value.OriginalFence is { } presentvalueOriginalFence ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalFence) : ReferenceFieldBytes) + (value.NextOriginalPhaseCommandId is not null ? GuidFieldBytes : ReferenceFieldBytes)
            + (value.NextOriginalPhase is { } presentvalueNextOriginalPhase ? PartitionMovementParentCapacityBounds.Bound(presentvalueNextOriginalPhase) : ReferenceFieldBytes) + (value.NextOriginalAuthorization is { } presentvalueNextOriginalAuthorization ? PartitionMovementParentCapacityBounds.Bound(presentvalueNextOriginalAuthorization) : ReferenceFieldBytes)
            + DateTimeFieldBytes + DateTimeFieldBytes
            + (value.OriginalCaptureWitness is { } presentvalueOriginalCaptureWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalCaptureWitness) : ReferenceFieldBytes) + GuidFieldBytes
            + GuidFieldBytes + (value.OriginalOutcomeWitness is { } presentvalueOriginalOutcomeWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalOutcomeWitness) : ReferenceFieldBytes)
            + GuidFieldBytes + GuidFieldBytes
            + (value.OriginalReceiverIssuanceWitness is { } presentvalueOriginalReceiverIssuanceWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverIssuanceWitness) : ReferenceFieldBytes) + (value.OriginalReceiverSourceWitness is { } presentvalueOriginalReceiverSourceWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverSourceWitness) : ReferenceFieldBytes)
            + (value.OriginalReceiverIssuePacket is { } presentvalueOriginalReceiverIssuePacket ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalReceiverIssuePacket) : ReferenceFieldBytes) + ScalarFieldBytes
            + (value.RetireCancellation is { } presentvalueRetireCancellation ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellation) : ReferenceFieldBytes) + (value.RetireCancellationAttempt is { } presentvalueRetireCancellationAttempt ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellationAttempt) : ReferenceFieldBytes));

    internal static long Bound(PartitionMoveParentCancellation value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.CancellationPhase) + PartitionMovementParentCapacityBounds.Bound(value.CancellationReceipt)
            + ScalarFieldBytes);

    internal static long Bound(PartitionMoveParentHeader value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.OriginalTransferRequest)
            + PartitionMovementParentCapacityBounds.Bound(value.OperatorPrincipalId) + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner) + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement)
            + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner) + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + (value.PendingOriginalPhaseCommandId is not null ? GuidFieldBytes : ReferenceFieldBytes) + (value.TerminalResult is { } presentvalueTerminalResult ? PartitionMovementParentCapacityBounds.Bound(presentvalueTerminalResult) : ReferenceFieldBytes)
            + (value.TerminalObservationReceipt is { } presentvalueTerminalObservationReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueTerminalObservationReceipt) : ReferenceFieldBytes) + GuidFieldBytes
            + (value.OriginalSourceOwner is { } presentvalueOriginalSourceOwner ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalSourceOwner) : ReferenceFieldBytes) + (value.InterruptedOriginalPhaseCommandId is not null ? GuidFieldBytes : ReferenceFieldBytes)
            + ScalarFieldBytes + (value.OriginalCapturePhaseCommandId is not null ? GuidFieldBytes : ReferenceFieldBytes));

    internal static long Bound(PartitionMovePhaseCommand value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement) + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest) + ScalarFieldBytes
            + ScalarFieldBytes + Bytes(value.Body.Length)
            + (value.GrantId is not null ? GuidFieldBytes : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.Resources)
            + (value.ReceiverEffectAdmission is { } presentvalueReceiverEffectAdmission ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverEffectAdmission) : ReferenceFieldBytes));

    internal static long Bound(PartitionMoveParentState value)
        => checked(ReferenceFieldBytes
            + (value.Header is { } presentvalueHeader ? PartitionMovementParentCapacityBounds.Bound(presentvalueHeader) : ReferenceFieldBytes) + (value.Pending is { } presentvaluePending ? PartitionMovementParentCapacityBounds.Bound(presentvaluePending) : ReferenceFieldBytes)
            + (value.LastIssued is { } presentvalueLastIssued ? PartitionMovementParentCapacityBounds.Bound(presentvalueLastIssued) : ReferenceFieldBytes) + (value.Selected is { } presentvalueSelected ? PartitionMovementParentCapacityBounds.Bound(presentvalueSelected) : ReferenceFieldBytes)
            + (value.Control is { } presentvalueControl ? PartitionMovementParentCapacityBounds.Bound(presentvalueControl) : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.Directory)
            + PartitionMovementParentCapacityBounds.Bound(value.Placement) + ScalarFieldBytes
            + (value.Interrupted is { } presentvalueInterrupted ? PartitionMovementParentCapacityBounds.Bound(presentvalueInterrupted) : ReferenceFieldBytes) + (value.CancellationOutcome is { } presentvalueCancellationOutcome ? PartitionMovementParentCapacityBounds.Bound(presentvalueCancellationOutcome) : ReferenceFieldBytes)
            + (value.SelectedOriginalOutcome is { } presentvalueSelectedOriginalOutcome ? PartitionMovementParentCapacityBounds.Bound(presentvalueSelectedOriginalOutcome) : ReferenceFieldBytes) + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.CurrentOperatorPrincipalId));
}
