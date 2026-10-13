using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Persists original native phase scope before dispatch and actual observation before advancing.</summary>
internal sealed class PartitionMovementParentCheckpointOwner(PartitionMovementReceiver receiver,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    internal async Task<PartitionMovePhaseResult> SubmitAsync(PartitionMoveParentState state,
        PartitionMoveCheckpointBody body, PartitionMovePhaseCommand original, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var digest = IntentDigest(state, body, original);
        var header = state.Header;
        var envelope = new PartitionMovePeerEnvelope(PartitionMoveProtocol.Version, body.OriginalTransferRequest.MoveId,
            body.OriginalTransferRequest.Partition, header?.ControlOwner ?? original.ControlOwner,
            header?.SourcePlacement ?? original.SourcePlacement, header?.DestinationOwner ?? original.DestinationOwner,
            digest, PartitionMovePeerStage.ControlCheckpoint, PartitionMovementProtocol.InitialPhaseOrdinal, PartitionMovementParentDeadline.Expiry(work, clock, routing),
            Guid.NewGuid(), NativeSerialization.Serialize(body));
        var reply = await receiver.ApplyParentPhaseAsync(Guid.NewGuid(), envelope, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } code)
        { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovePhaseResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        if (actual.Stage != PartitionMovePeerStage.ControlCheckpoint || actual.MoveId != body.OriginalTransferRequest.MoveId
            || actual.Journal.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition || actual.Journal.ControlIntentDigest != digest)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return actual;
    }

    internal static string IntentDigest(PartitionMoveParentState state, PartitionMoveCheckpointBody body,
        PartitionMovePhaseCommand original)
    {
        if (body.Action == PartitionMoveCheckpointAction.Observe
            && PartitionMoveGrantValidation.IsLocalControl(original.Stage)
            && body.ObservedOriginalResult is { Error: null } observed)
        {
            var actual = observed.Get<PartitionMovePhaseResult>();
            var control = state.Header?.ControlOwner ?? original.ControlOwner;
            if (actual.MoveId != original.MoveId || actual.MoveId != body.OriginalTransferRequest.MoveId
                || actual.Stage != original.Stage || actual.Journal.CommandId != body.OriginalPhaseCommandId
                || actual.Journal.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition
                || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, control)
                || !global::KeyLoad.Core.Features.ClusterRouting.Validation.PartitionMoveSourceFenceValidation
                    .ValidDigest(actual.Journal.ControlIntentDigest)
                || actual.Control is { } committed
                    && PartitionMoveIntentIdentity.Digest(committed) != actual.Journal.ControlIntentDigest)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
            return actual.Journal.ControlIntentDigest;
        }
        return state.Control is { } current
            ? PartitionMoveIntentIdentity.Digest(current) : original.ControlIntentDigest;
    }

    internal static PartitionMovePeerEnvelope OriginalEnvelope(PartitionMoveParentPhase original, bool release,
        PartitionMoveReceiverSourceWitness? dispatch = null)
    {
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var local = PartitionMoveGrantValidation.IsLocalControl(phase.Stage);
        if (!local && original.OriginalRequestNonce == Guid.Empty
            || release && (phase.Stage != PartitionMovePeerStage.Capture || original.OriginalCaptureReleaseNonce == Guid.Empty))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var nonce = local ? Guid.NewGuid() : original.OriginalRequestNonce;
        return new(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal,
            original.OriginalExpiresAt, release ? original.OriginalCaptureReleaseNonce
                : nonce,
            phase.Body, original.OriginalGrant, dispatch is null ? null : original.OriginalReceiverIssuanceWitness, dispatch);
    }
}
