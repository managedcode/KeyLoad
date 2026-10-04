namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlShape
{
    internal static bool Valid(ICacheControlMessage? message)
        => message switch
        {
            CacheReadyProof proof => CacheControlValidation.Proof(proof),
            CachePrepareRequest request => CacheControlRequestValidation.Prepare(request),
            CacheGrantRequest request => CacheControlRequestValidation.Grant(request),
            CacheRevokeRequest request => CacheControlRequestValidation.Revoke(request),
            CacheRefreshHint request => CacheControlRequestValidation.Refresh(request),
            CachePrepareReply reply => CacheControlReplyValidation.Prepare(reply),
            CacheGrantReply reply => CacheControlReplyValidation.Grant(reply),
            CacheRevokeReply reply => CacheControlReplyValidation.Revoke(reply),
            CacheRefreshReceipt reply => CacheControlReplyValidation.Refresh(reply),
            _ => false
        };

    // Only known sealed shapes can reach this getter after Valid has succeeded.
    internal static bool IsFlat(ICacheControlMessage message)
        => message is ICacheControlReply { Correlation: null };

    internal static CacheControlDigest Scope(ICacheControlMessage message)
        => message switch
        {
            CacheReadyProof proof => proof.ScopeHash,
            ICacheControlRequest request => request.Header.ScopeHash,
            ICacheControlReply { Correlation: { } correlation } => correlation.Header.ScopeHash,
            _ => default
        };
}
