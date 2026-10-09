using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveTransferDataRead ReadVerifiedPartitionMovementTransferData(string localPrincipalId,
        ReadOnlyMemory<byte> originalControlReply, string signature, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var verified = VerifyPartitionMovementTransferReadAuthority(localPrincipalId, originalControlReply, signature, work);
        var phase = verified.CapturePhase;
        var original = phase.OriginalPhase!;
        var envelope = new PartitionMovePeerEnvelope(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            original.Stage, original.PageOrdinal, phase.OriginalExpiresAt, phase.OriginalRequestNonce,
            original.Body, phase.OriginalGrant);
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, Limits.MaxBatchBytes);
        var capture = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(original.Body.Span);
        RequireTransferDataCaptureBounds(capture);
        var identity = OriginalMovePhaseIdentity(envelope, phase.OriginalPhaseCommandId, localPrincipalId);
        return Store.Read(view =>
        {
            RequireTransferDataSourceScope(view, localPrincipalId, verified, envelope, capture, identity, work);
            return PartitionMoveTransferDataReader.Read(view, phase.OriginalFence!, phase.OriginalDescriptor!, work,
                Limits, capture.MaximumPageBytes, capture.MaximumImageBytes, capture.MaximumRecords);
        });
    }

    internal void ValidatePartitionMovementTransferReadSession(string principalId,
        ReadOnlyMemory<byte> originalControlReply, string signature, ReadExecutionBudget work)
    {
        var verified = VerifyPartitionMovementTransferReadAuthority(principalId, originalControlReply, signature, work);
        var phase = verified.CapturePhase;
        var original = phase.OriginalPhase!;
        var envelope = new PartitionMovePeerEnvelope(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            original.Stage, original.PageOrdinal, phase.OriginalExpiresAt, phase.OriginalRequestNonce,
            original.Body, phase.OriginalGrant);
        var capture = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(original.Body.Span);
        var identity = OriginalMovePhaseIdentity(envelope, phase.OriginalPhaseCommandId, principalId);
        Store.Read(view =>
        {
            RequireTransferDataSourceScope(view, principalId, verified, envelope, capture, identity, work);
            var fence = PartitionMoveSourceFenceStorage.Read(work.CreateView(view), phase.Partition, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            PartitionMoveImageCapture.RequireFence(view, phase.OriginalFence!, fence, work);
            return true;
        });
        work.Check();
    }

    private void RequireTransferDataSourceScope(KeyLoad.Storage.IKeyValueView view, string localPrincipalId,
        PartitionMoveTransferReadAuthority verified, PartitionMovePeerEnvelope envelope,
        PartitionMoveCaptureRequest capture, string identity, ReadExecutionBudget work)
    {
        var phase = verified.CapturePhase;
        var admitted = work.CreateView(view);
        var actual = ResolveMovePhaseOutcome(admitted, envelope, phase.OriginalPhaseCommandId, localPrincipalId, identity);
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(phase.OriginalResult)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        PartitionMoveCleanupStorage.RequireOpen(admitted, envelope.Partition, envelope.MoveId, Limits.MaxBatchBytes);
        if (!NativeSerialization.Serialize(capture.Fence).AsSpan().SequenceEqual(NativeSerialization.Serialize(phase.OriginalFence)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    internal PartitionMoveTransferReadAuthority VerifyPartitionMovementTransferReadAuthority(string principalId,
        ReadOnlyMemory<byte> originalControlReply, string signature, ReadExecutionBudget work)
    {
        work.Check();
        if (originalControlReply.IsEmpty || originalControlReply.Length > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var originalBytes = originalControlReply.ToArray();
        var authorityBytes = movementCheckpointVerifier.VerifyTransferRead(this, principalId, originalBytes, signature, work);
        var verified = NativeSerialization.Deserialize<PartitionMoveTransferReadAuthority>(authorityBytes.Span);
        PartitionMoveTransferAuthorityValidation.Require(verified, Limits);
        work.CheckResult(verified);
        return verified;
    }

    internal PartitionMoveTransferReadAuthority VerifyPartitionMovementTransferCleanupAuthority(string principalId,
        ReadOnlyMemory<byte> originalControlReply, string signature, ReadExecutionBudget work)
    {
        work.Check();
        if (originalControlReply.IsEmpty || originalControlReply.Length > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var originalBytes = originalControlReply.ToArray();
        var authorityBytes = movementCheckpointVerifier.VerifyTransferCleanup(this, principalId, originalBytes, signature, work);
        var verified = NativeSerialization.Deserialize<PartitionMoveTransferReadAuthority>(authorityBytes.Span);
        PartitionMoveTransferAuthorityValidation.Require(verified, Limits);
        work.CheckResult(verified);
        return verified;
    }

    private void RequireTransferDataCaptureBounds(PartitionMoveCaptureRequest capture)
    {
        if (capture.MaximumPageBytes <= PartitionMoveProtocol.EmptyCount || capture.MaximumPageBytes > Limits.MaxBatchBytes
            || capture.MaximumImageBytes <= PartitionMoveProtocol.EmptyCount || capture.MaximumImageBytes > Limits.MaxQueryReadBytes
            || capture.MaximumRecords <= PartitionMoveProtocol.EmptyCount || capture.MaximumRecords > Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }
}
