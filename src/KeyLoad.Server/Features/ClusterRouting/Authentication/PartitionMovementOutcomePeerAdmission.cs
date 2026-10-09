using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementOutcomePeerAdmission
{
    internal static async Task<PartitionMovementOutcomeTransportRequest> VerifyOutcomeAsync(PartitionMovementPeerAdmission peer,
        ReadOnlyMemory<byte> originalBody, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!peer.Execution.Enabled || !peer.Options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof); }
        if (originalBody.Length <= PartitionMovementPeerAdmission.EmptyBodyBytes || originalBody.Length > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var ownedBody = originalBody.ToArray();
        using var mac = peer.CreateControlMac();
        if (!mac.VerifyOutcome(ownedBody, signature, reply: false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var query = NativeSerialization.Deserialize<PartitionMovementOutcomeTransportRequest>(ownedBody);
        var now = peer.Clock.GetUtcNow();
        PartitionMovementOutcomeAdmission.Require(query, peer.Database.Limits, now, peer.Routing.RequestLifetime);
        var request = query.Original;
        var envelope = request.Envelope;
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, peer.Database.Limits.MaxBatchBytes);
        var control = PhysicalOwnerConfiguredTuples.Control(peer.Options, peer.Configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(peer.Options, peer.Configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(peer.Options, peer.Configuration);
        if (!PhysicalOwnerEntryValidation.SameOwner(envelope.ControlOwner, control.Owner)
            || !PartitionMovementConfiguredOwners.RequirePair(envelope, control.Owner, destination.Owner)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(request.CallerSiloAddress, peer.Membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        RequireOutcomeJournal(request, control.Owner, local.Owner);
        var address = SiloAddress.FromParsableString(request.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await peer.Pins.PinCallerAsync(control.Owner.VoterIds.IndexOf(request.CallerVoter), address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
        if (!peer.Replay.TryUse(query.Nonce.ToString(PartitionMovementPeerAdmission.NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        peer.Database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return query;
    }

    private static void RequireOutcomeJournal(PartitionMovementTransportRequest request,
        PhysicalShardRecord control, PhysicalShardRecord local)
    {
        if (PartitionMoveGrantValidation.IsLocalControl(request.Envelope.Stage))
        {
            if (request.Envelope.Grant is not null || request.ControlGrantJournal is not null
                || !PhysicalOwnerEntryValidation.SameOwner(control, local))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
            return;
        }
        var grant = request.Envelope.Grant;
        var journal = request.ControlGrantJournal;
        if (grant is null || journal is null || grant.PhaseCommandId != request.CommandId
            || journal.CommandId != grant.GrantId || journal.AppliedPosition != grant.AdmissionPosition
            || journal.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(journal.PhysicalOwner, control)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, local))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
    }
}
