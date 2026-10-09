using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

/// <summary>Rejects inconsistent current-format parent authority without reconstructing older rows.</summary>
internal static class PartitionMoveParentValidation
{
    private const int MinimumPlacementRevision = 0;
    private const int UnissuedPolicyEpoch = 0;
    private const int UnestablishedCheckpointGeneration = 0;
    private const int InitialCleanupGeneration = 0;
    private const int EmptyRetainedPhaseCount = 0;
    private const int EmptyRetainedMetadataBytes = 0;
    private const int FirstPageOrdinal = 0;
    private const int UnappliedPosition = 0;

    internal static void RequireHeader(PartitionMoveParentHeader value, PartitionRef partition, Guid moveId)
    {
        if (value.Version != PartitionMoveProtocol.Version || value.Partition != partition || value.MoveId != moveId
            || moveId == Guid.Empty || value.OriginalTransferRequest.MoveId != moveId
            || value.OriginalTransferRequest.Partition != partition
            || value.OriginalTransferRequest.Mode != PartitionMoveMode.Transfer
            || value.OriginalTransferRequest.DestinationPhysicalShardId != value.DestinationOwner.PhysicalShardId
            || value.OriginalTransferRequest.ExpectedPlacementRevision < MinimumPlacementRevision
            || string.IsNullOrWhiteSpace(value.OperatorPrincipalId) || value.InitialPolicyEpoch <= UnissuedPolicyEpoch
            || value.Generation <= UnestablishedCheckpointGeneration || value.CleanupGeneration < InitialCleanupGeneration
            || value.OriginalCapturePhaseCommandId == Guid.Empty || value.RetainedPhaseCount < EmptyRetainedPhaseCount || value.RetainedMetadataBytes <= EmptyRetainedMetadataBytes
            || value.LastOriginalPhaseCommandId == Guid.Empty || value.OriginalSourceOwner is null
            || value.OriginalSourceOwner.PhysicalShardId != value.SourcePlacement.PhysicalShardId
            || value.OriginalSourceOwner.Incarnation != value.SourcePlacement.Incarnation
            || !value.OriginalSourceOwner.VoterIds.SequenceEqual(value.SourcePlacement.VoterIds, StringComparer.Ordinal)
            || value.InterruptedOriginalPhaseCommandId == Guid.Empty
            || value.InterruptedOriginalPhaseCommandId is not null
                && value.InterruptedOriginalPhaseCommandId == value.PendingOriginalPhaseCommandId
            || value.PendingOriginalPhaseCommandId == Guid.Empty
            || (value.TerminalResult is null) != (value.TerminalObservationReceipt is null)
            || value.TerminalResult is not null && value.PendingOriginalPhaseCommandId is not null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }

    internal static void RequirePhase(PartitionMoveParentPhase value, PartitionRef partition, Guid moveId,
        Guid originalPhaseId)
    {
        if (value.Version != PartitionMoveProtocol.Version || value.Partition != partition || value.MoveId != moveId
            || value.OriginalPhaseCommandId != originalPhaseId || originalPhaseId == Guid.Empty
            || !Enum.IsDefined(value.Stage) || value.Stage is PartitionMovePeerStage.ControlCheckpoint or PartitionMovePeerStage.RetireCancel
            || value.PageOrdinal < FirstPageOrdinal || value.CleanupGeneration < InitialCleanupGeneration
            || value.CleanupGeneration != InitialCleanupGeneration && value.Stage is not (PartitionMovePeerStage.Retire
                or PartitionMovePeerStage.ControlAuthorize or PartitionMovePeerStage.ControlAcknowledge)
            || !PartitionMoveSourceFenceValidation.ValidDigest(value.OriginalPhaseIdentityDigest)
            || string.IsNullOrWhiteSpace(value.OriginalBodyDigest)
            || value.OriginalExpiresAt == default || value.OriginalIssuancePolicyEpoch <= UnissuedPolicyEpoch
            || value.OriginalGrant is not null && value.OriginalGrant.OperatorPolicyEpoch != value.OriginalIssuancePolicyEpoch
            || value.AdmissionCheckpointReceipt.AppliedPosition <= UnappliedPosition
            || (!PartitionMoveGrantValidation.IsLocalControl(value.Stage)) != (value.OriginalRequestNonce != Guid.Empty)
            || (value.Stage == PartitionMovePeerStage.Capture) != (value.OriginalCaptureReleaseNonce != Guid.Empty)
            || value.Stage == PartitionMovePeerStage.Capture && value.OriginalCaptureReleaseNonce == value.OriginalRequestNonce
            || value.OriginalCaptureWitness is not null && value.Stage != PartitionMovePeerStage.Capture
            || (value.OriginalCaptureWitness is null) != (value.CaptureProofCheckpointReceipt is null)
            || value.OriginalCaptureWitness is not null && (value.OriginalDescriptor is null || value.OriginalFence is null)
            || value.RetireCancellation is null
                && (value.OriginalResult is null) != (value.ObservationCheckpointReceipt is null)
            || value.OriginalPhase is null && value.OriginalResult is null
            || InvalidCancellation(value, originalPhaseId)
            || value.OriginalOutcomeWitness is not null && value.OriginalResult is null
            || !PartitionMoveGrantValidation.IsLocalControl(value.Stage) && value.OriginalResult is not null
                && value.OriginalOutcomeWitness is null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        if (value.OriginalPhase is { } phase && (phase.MoveId != moveId || phase.Partition != partition
            || phase.Stage != value.Stage || phase.PageOrdinal != value.PageOrdinal
            || BodyDigest(phase) != value.OriginalBodyDigest
            || PartitionMoveOriginalDispatchIdentity.Digest(value.OriginalPhaseCommandId, phase,
                value.OriginalGrant, value.OriginalExpiresAt) != value.OriginalPhaseIdentityDigest))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }

    private static bool InvalidCancellation(PartitionMoveParentPhase value, Guid originalPhaseId)
        => value.Cancellation is not null && (value.Stage != PartitionMovePeerStage.ControlPrepare
                || value.OriginalPhase is null || value.OriginalResult is not null || value.OriginalGrant is not null
                || value.Cancellation.Version != PartitionMoveProtocol.Version
                || value.Cancellation.OriginalPhaseCommandId != originalPhaseId
                || value.Cancellation.CancellationReceipt.CommandId == Guid.Empty
                || value.Cancellation.CancellationReceipt.CommandId == originalPhaseId
                || value.Cancellation.CancellationReceipt.AppliedPosition <= UnappliedPosition
                || value.Cancellation.CancellationPolicyEpoch <= UnissuedPolicyEpoch)
            || value.RetireCancellationAttempt is { } attempt && (value.Stage != PartitionMovePeerStage.Retire
                || attempt.Version != PartitionMoveProtocol.Version || attempt.CancellationCommandId == Guid.Empty
                || attempt.CancellationCommandId == originalPhaseId || attempt.CancellationNonce == Guid.Empty
                || attempt.CancellationNonce == value.OriginalRequestNonce || attempt.CancellationExpiresAt == default
                || attempt.OriginalRequestBytes.IsEmpty || string.IsNullOrWhiteSpace(attempt.OriginalRequestSignature))
            || value.RetireCancellation is { } cancelled && (value.Stage != PartitionMovePeerStage.Retire
                || value.RetireCancellationAttempt is null || value.OriginalResult is not null
                || value.OriginalPhase is null || value.ObservationCheckpointReceipt is null
                || cancelled.Cancellation.Version != PartitionMoveProtocol.Version
                || cancelled.Cancellation.OriginalPhaseCommandId != originalPhaseId
                || cancelled.Cancellation.OriginalPhaseIdentityDigest != value.OriginalPhaseIdentityDigest
                || cancelled.Cancellation.OriginalRequestNonce != value.OriginalRequestNonce
                || cancelled.Cancellation.OriginalExpiresAt != value.OriginalExpiresAt
                || cancelled.Cancellation.CleanupGeneration != value.CleanupGeneration
                || cancelled.Cancellation.CancellationCommandId != value.RetireCancellationAttempt.CancellationCommandId
                || cancelled.OriginalReplyBytes.IsEmpty || string.IsNullOrWhiteSpace(cancelled.OriginalReplySignature));

    internal static string BodyDigest(PartitionMovePhaseCommand phase)
        => Convert.ToHexStringLower(SHA256.HashData(phase.Body.Span));

    internal static void RequireReplayScope(PartitionMoveParentHeader header, string principalId,
        PartitionMoveRequest request)
    {
        var original = header.OriginalTransferRequest;
        if (header.OperatorPrincipalId != principalId || original.MoveId != request.MoveId
            || original.Partition != request.Partition
            || original.DestinationPhysicalShardId != request.DestinationPhysicalShardId
            || original.ExpectedPlacementRevision != request.ExpectedPlacementRevision
            || !Enum.IsDefined(request.Mode) || request.Mode == PartitionMoveMode.None)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }
}
