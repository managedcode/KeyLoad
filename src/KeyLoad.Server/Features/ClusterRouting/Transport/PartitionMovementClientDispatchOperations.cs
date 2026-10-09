using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientDispatchOperations
{
    internal static async Task<PartitionMovementDispatchResult> DispatchAsync(PartitionMovementClient owner, Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PartitionMovementClientAdmissionOperations.RequireSenderAdmission();
        return await PartitionMovementClientDispatchOperations.DispatchAdmittedAsync(owner, phaseCommandId, original, authorization, action, handleId,
            ordinal, pinnedVoter, Guid.Empty, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartitionMovementDispatchResult> DispatchParentOriginalAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        PartitionMoveReceiverSourceWitness dispatch, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PartitionMovementClientParentAdmissionOperations.RequireParentSenderAsync(owner, original, dispatch, work, cancellationToken).ConfigureAwait(false);
        if (original.OriginalResult is not null || original.Stage == PartitionMovePeerStage.Capture
            || PartitionMoveGrantValidation.IsLocalControl(original.Stage)
            || original.OriginalRequestNonce == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return await PartitionMovementClientDispatchOperations.DispatchAdmittedAsync(owner, original.OriginalPhaseCommandId,
            PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false, dispatch),
            original.OriginalAuthorization, PartitionMovementPeerAction.Apply, Guid.Empty, original.PageOrdinal,
            null, original.OriginalRequestNonce, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartitionMovementDispatchResult> CaptureParentOriginalAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        PartitionMoveReceiverSourceWitness dispatch, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PartitionMovementClientParentAdmissionOperations.RequireParentSenderAsync(owner, original, dispatch, work, cancellationToken).ConfigureAwait(false);
        if (original.Stage != PartitionMovePeerStage.Capture || original.OriginalResult is not null
            || original.CaptureProofCheckpointReceipt is not null || original.OriginalRequestNonce == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return await PartitionMovementClientDispatchOperations.DispatchAdmittedAsync(owner, original.OriginalPhaseCommandId,
            PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false, dispatch),
            original.OriginalAuthorization, PartitionMovementPeerAction.Capture, Guid.Empty,
            original.PageOrdinal, null, original.OriginalRequestNonce, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartitionMovementDispatchResult> ReleaseParentCaptureAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        Guid handleId, string originalVoter, PartitionMoveReceiverSourceWitness dispatch,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PartitionMovementClientParentAdmissionOperations.RequireParentSenderAsync(owner, original, dispatch, work, cancellationToken).ConfigureAwait(false);
        if (original.Stage != PartitionMovePeerStage.Capture || original.OriginalResult is not null
            || original.CaptureProofCheckpointReceipt is null || original.OriginalCaptureWitness is null
            || original.OriginalDescriptor is null || original.OriginalFence is null
            || original.OriginalCaptureReleaseNonce == Guid.Empty
            || original.OriginalCaptureReleaseNonce == original.OriginalRequestNonce || handleId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return await PartitionMovementClientDispatchOperations.DispatchAdmittedAsync(owner, original.OriginalPhaseCommandId,
            PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: true, dispatch),
            original.OriginalAuthorization, PartitionMovementPeerAction.Release, handleId,
            original.PageOrdinal, originalVoter, original.OriginalCaptureReleaseNonce, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartitionMovementDispatchResult> DispatchAdmittedAsync(PartitionMovementClient owner, Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        Guid originalRequestNonce, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originalRequestNonce != Guid.Empty && action is not (PartitionMovementPeerAction.Apply
            or PartitionMovementPeerAction.Capture or PartitionMovementPeerAction.Release))
        { throw Errors.Fail(ErrorCode.Validation, PartitionMovementProtocol.InvalidProof); }
        if (owner.options.Value.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !owner.options.Value.MembershipAuthority.RegisterPhysicalOwners || original.ExpiresAt <= owner.clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var receiver = original.Grant?.ReceiverOwner ?? original.ControlOwner;
        var control = PhysicalOwnerConfiguredTuples.Control(owner.options.Value, owner.partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(owner.options.Value, owner.partition);
        var local = PhysicalOwnerEntryValidation.SameOwner(receiver, control.Owner);
        var selected = local ? control : destination;
        if (!PhysicalOwnerEntryValidation.SameOwner(receiver, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var index = PartitionMovementClientDispatchOperations.SelectVoter(selected, action, pinnedVoter);
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var admittedEnvelope = original with
        {
            Nonce = originalRequestNonce == Guid.Empty
            ? Guid.NewGuid() : originalRequestNonce
        };
        var request = new PartitionMovementTransportRequest(phaseCommandId, admittedEnvelope, authorization,
            owner.partition.Configuration.LocalId, discovery.SiloAddress,
            (PartitionMovementTransportAction)action, handleId, ordinal);
        PartitionMovementTransportAdmission.Require(request);
        var size = NativeSerialization.Measure(request);
        if (size < PartitionMovementClient.MinimumBodyBytes || size > owner.partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var encoded = NativeSerialization.Serialize(request);
        var reply = await owner.transport.ExchangeAsync(index, selected, request, encoded, local, cancellationToken).ConfigureAwait(false);
        var witness = action == PartitionMovementPeerAction.Capture
            ? new PartitionMoveCaptureWitness(PartitionMoveProtocol.Version, phaseCommandId,
                request.Envelope.Nonce, reply.OriginalBytes, reply.Signature) : null;
        var outcome = action is PartitionMovementPeerAction.Apply or PartitionMovementPeerAction.Release
            ? new PartitionMoveAuthenticatedOutcomeWitness(PartitionMoveProtocol.Version,
                PartitionMoveOutcomeProofPurpose.EffectReply, phaseCommandId, request.Envelope.Nonce,
                reply.OriginalBytes, reply.Signature) : null;
        return new(reply.Value.Discovery.VoterId, reply.Value.Reply, witness, outcome);
    }

    internal static int SelectVoter(RegisteredPhysicalOwnerV1 receiver, PartitionMovementPeerAction action,
        string? pinnedVoter)
    {
        if (action is PartitionMovementPeerAction.Page or PartitionMovementPeerAction.Release
            || action == PartitionMovementPeerAction.Capture && pinnedVoter is not null)
        {
            var index = receiver.Owner.VoterIds.IndexOf(pinnedVoter!);
            if (pinnedVoter is null || index < PartitionMovementClient.FirstVoter)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
            return index;
        }
        if (pinnedVoter is not null)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMovementProtocol.InvalidProof); }
        return PartitionMovementClient.FirstVoter;
    }
}
