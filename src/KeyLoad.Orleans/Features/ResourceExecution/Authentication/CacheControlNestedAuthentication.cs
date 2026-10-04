namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlNestedAuthentication
{
    internal static bool Verify(ReadOnlySpan<byte> key, CacheControlDigest scope, ICacheControlMessage message)
        => message switch
        {
            CacheGrantRequest request => Proof(key, scope, request.Slot0Proof)
                && Proof(key, scope, request.Slot1Proof) && Proof(key, scope, request.Slot2Proof),
            CachePrepareReply { Proof: { } proof } => Proof(key, scope, proof),
            _ => true
        };

    private static bool Proof(ReadOnlySpan<byte> key, CacheControlDigest scope, CacheReadyProof proof)
    {
        return !proof.Mac.FixedTimeEquals(default) && scope.FixedTimeEquals(proof.ScopeHash)
            && CacheControlWire.TryEncodeForSigning(proof, out var transcript)
            && proof.Mac.FixedTimeEquals(CacheControlMac.Compute(key, transcript));
    }
}
