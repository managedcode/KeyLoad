using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireAuthenticatorSupport
{
    internal static CacheReadyProof SignProof(CacheControlAuthenticator authenticator, CacheVoterSlot origin, CacheVoterSlot binding)
    {
        var success = authenticator.TrySign(CacheControlWireTestData.Proof(origin, binding), out var signed);
        return RequireSigned(success, signed);
    }

    internal static CachePrepareRequest SignPrepare(CacheControlAuthenticator authenticator, CachePrepareRequest request)
    {
        var success = authenticator.TrySign(request, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheGrantRequest SignGrant(CacheControlAuthenticator authenticator, CacheGrantRequest request)
    {
        var slot0 = SignProof(authenticator, request.Slot0Proof);
        var slot1 = SignProof(authenticator, request.Slot1Proof);
        var slot2 = SignProof(authenticator, request.Slot2Proof);
        var withProofs = request with { Slot0Proof = slot0, Slot1Proof = slot1, Slot2Proof = slot2 };
        var success = authenticator.TrySign(withProofs, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheRevokeRequest SignRevoke(CacheControlAuthenticator authenticator, CacheRevokeRequest request)
    {
        var success = authenticator.TrySign(request, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheRefreshHint SignRefresh(CacheControlAuthenticator authenticator, CacheRefreshHint request)
    {
        var success = authenticator.TrySign(request, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CachePrepareReply SignPrepareReply(CacheControlAuthenticator authenticator, CachePrepareRequest request,
        CacheReadyProof? proof, CacheControlStatus status = CacheControlStatus.Ready)
    {
        var reply = new CachePrepareReply(Correlation(request), status, proof, CacheControlWireTestData.NondefaultMac);
        var success = authenticator.TrySign(reply, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheGrantReply SignGrantReply(CacheControlAuthenticator authenticator, CacheGrantRequest request)
    {
        var sequence = TargetProof(request).ChallengeSequence;
        var reply = new CacheGrantReply(Correlation(request), CacheControlStatus.AcceptedActive,
            request.GrantId, request.TargetBinding, sequence, CacheControlWireTestData.NondefaultMac);
        var success = authenticator.TrySign(reply, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheGrantReply SignGrantReply(CacheControlAuthenticator authenticator, CacheGrantRequest request,
        CacheControlStatus status, long acceptedSequence)
    {
        var reply = new CacheGrantReply(Correlation(request), status,
            request.GrantId, request.TargetBinding, acceptedSequence, CacheControlWireTestData.NondefaultMac);
        var success = authenticator.TrySign(reply, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheRevokeReply SignRevokeReply(CacheControlAuthenticator authenticator, CacheRevokeRequest request)
    {
        var reply = new CacheRevokeReply(Correlation(request), CacheControlStatus.Revoked,
            request.GrantId, CacheRevokeEffect.Both, CacheControlWireTestData.NondefaultMac);
        var success = authenticator.TrySign(reply, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheRefreshReceipt SignRefreshReply(CacheControlAuthenticator authenticator, CacheRefreshHint request)
    {
        var reply = new CacheRefreshReceipt(Correlation(request), CacheControlStatus.HintAcknowledged,
            CacheControlWireTestData.SessionId, CacheControlWireTestData.RoundNonce,
            CacheControlWireTestData.Address(CacheVoterSlot.Slot0), CacheControlWireTestData.NondefaultMac);
        var success = authenticator.TrySign(reply, out var signed);
        return RequireSigned(success, signed);
    }

    internal static CacheReplyCorrelation Correlation(ICacheControlRequest request)
    {
        var success = CacheControlCorrelation.TryCreate(request, out var correlation);
        return RequireSigned(success, correlation);
    }

    internal static T RequireSigned<T>(bool success, T? signed) where T : class
        => success && signed is not null ? signed : throw new InvalidOperationException(SigningFailureMessage);

    internal static CacheControlAuthenticator CreateAuthenticator()
        => new(PeerKey(0x21), CacheControlWireTestData.ScopeHash);

    private static CacheReadyProof SignProof(CacheControlAuthenticator authenticator, CacheReadyProof proof)
    {
        var success = authenticator.TrySign(proof, out var signed);
        return RequireSigned(success, signed);
    }

    private static CacheReadyProof TargetProof(CacheGrantRequest request)
        => request.Header.TargetSlot switch
        {
            CacheVoterSlot.Slot0 => request.Slot0Proof,
            CacheVoterSlot.Slot1 => request.Slot1Proof,
            CacheVoterSlot.Slot2 => request.Slot2Proof,
            _ => throw new InvalidOperationException(SigningFailureMessage)
        };

    internal static byte[] PeerKey(byte seed)
        => Enumerable.Range(0, PeerKeyLength).Select(index => (byte)(seed + index)).ToArray();

    private const int PeerKeyLength = 32;
    private const string SigningFailureMessage = "A valid frozen cache control message failed to sign or correlate.";
}
