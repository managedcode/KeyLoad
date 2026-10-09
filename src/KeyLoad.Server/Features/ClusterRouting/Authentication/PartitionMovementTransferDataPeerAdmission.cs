using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementTransferDataPeerAdmission
{
    internal static async Task<PartitionMovementTransferDataRequest> VerifyTransferDataAsync(PartitionMovementPeerAdmission peer, ReadOnlyMemory<byte> originalBody,
        string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!peer.Execution.Enabled || !peer.Options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof); }
        if (originalBody.IsEmpty || originalBody.Length > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var ownedBody = originalBody.ToArray();
        using var mac = peer.CreateControlMac();
        if (!mac.VerifyTransferData(ownedBody, signature, reply: false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var request = NativeSerialization.Deserialize<PartitionMovementTransferDataRequest>(ownedBody);
        var now = peer.Clock.GetUtcNow();
        var control = PhysicalOwnerConfiguredTuples.Control(peer.Options, peer.Configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(peer.Options, peer.Configuration);
        if (request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
            || request.ExpiresAt <= now || request.ExpiresAt > now + peer.Routing.RequestLifetime
            || string.IsNullOrWhiteSpace(request.OperatorPrincipalId)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(request.CallerSiloAddress, peer.Membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var address = SiloAddress.FromParsableString(request.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await peer.Pins.PinCallerAsync(control.Owner.VoterIds.IndexOf(request.CallerVoter), address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
        if (!peer.Replay.TryUse(request.Nonce.ToString(PartitionMovementPeerAdmission.NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        peer.Database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return request;
    }
}
