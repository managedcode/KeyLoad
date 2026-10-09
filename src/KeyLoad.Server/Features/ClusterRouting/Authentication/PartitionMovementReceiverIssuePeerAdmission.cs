using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementReceiverIssuePeerAdmission
{
    internal static async Task<PartitionMovementReceiverIssueRequest> VerifyReceiverIssueAsync(PartitionMovementPeerAdmission peer, ReadOnlyMemory<byte> body,
        string signature, CancellationToken cancellationToken)
    {
        var owned = VerifyReceiverIssueMac(peer, body, signature, query: false);
        var request = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(owned);
        if (request.Version != PartitionMoveProtocol.Version
            || request.IssuanceCommandId != PartitionMoveReceiverIssuanceIdentity.For(request.OriginalPhaseCommandId)
            || request.IssuanceNonce != PartitionMovementReceiverIssueNonce.For(request.OriginalPhaseCommandId,
                request.SourceWitness.QueryNonce)
            || request.IssuanceNonce == request.SourceWitness.QueryNonce)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await RequireReceiverIssuePeerAsync(peer, request.OriginalPhaseCommandId, request.OriginalEnvelope,
            request.CallerVoter, request.CallerSiloAddress, request.IssuanceNonce,
            request.OriginalEnvelope.ExpiresAt, cancellationToken).ConfigureAwait(false);
        return request;
    }

    internal static async Task<PartitionMovementReceiverIssueQuery> VerifyReceiverIssueQueryAsync(PartitionMovementPeerAdmission peer, ReadOnlyMemory<byte> body,
        string signature, CancellationToken cancellationToken)
    {
        var owned = VerifyReceiverIssueMac(peer, body, signature, query: true);
        var request = NativeSerialization.Deserialize<PartitionMovementReceiverIssueQuery>(owned);
        if (request.Version != PartitionMoveProtocol.Version || request.MaximumReadBytes <= PartitionMovementProtocol.NoReadBytes
            || request.MaximumReadBytes > peer.Database.Limits.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= PartitionMovementProtocol.NoExaminedRecords || request.MaximumExaminedRecords > peer.Database.Limits.MaxScanRecords
            || request.MaximumResultBytes <= PartitionMovementProtocol.NoResultBytes || request.MaximumResultBytes > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await RequireReceiverIssuePeerAsync(peer, request.OriginalPhaseCommandId, request.OriginalEnvelope,
            request.CallerVoter, request.CallerSiloAddress, request.QueryNonce,
            request.QueryExpiresAt, cancellationToken).ConfigureAwait(false);
        return request;
    }

    internal static byte[] VerifyReceiverIssueMac(PartitionMovementPeerAdmission peer, ReadOnlyMemory<byte> body, string signature, bool query)
    {
        if (!peer.Execution.Enabled || !peer.Options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof); }
        if (body.IsEmpty || body.Length > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var owned = body.ToArray();
        using var mac = peer.CreateControlMac();
        if (!mac.VerifyReceiverIssueRequest(owned, signature, query))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        return owned;
    }

    internal static async Task RequireReceiverIssuePeerAsync(PartitionMovementPeerAdmission peer, Guid phaseId, PartitionMovePeerEnvelope original,
        string voter, string silo, Guid nonce, DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, peer.Database.Limits.MaxBatchBytes);
        var control = PhysicalOwnerConfiguredTuples.Control(peer.Options, peer.Configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(peer.Options, peer.Configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(peer.Options, peer.Configuration);
        var now = peer.Clock.GetUtcNow();
        if (phaseId == Guid.Empty || original.Grant is not { RequireReceiverIssuance: true } grant
            || grant.PhaseCommandId != phaseId || nonce == Guid.Empty || nonce == original.Nonce
            || expiry <= now || expiry > now + peer.Routing.RequestLifetime
            || !PhysicalOwnerEntryValidation.SameOwner(original.ControlOwner, control.Owner)
            || !PartitionMovementConfiguredOwners.RequirePair(original, control.Owner, destination.Owner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, local.Owner)
            || !control.Owner.VoterIds.Contains(voter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(silo, peer.Membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var address = SiloAddress.FromParsableString(silo);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await peer.Pins.PinCallerAsync(control.Owner.VoterIds.IndexOf(voter), address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
        if (!peer.Replay.TryUse(nonce.ToString(PartitionMovementPeerAdmission.NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        peer.Database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
    }
}
