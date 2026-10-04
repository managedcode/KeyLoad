namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlHeaderComparison
{
    internal static bool Exact(CacheControlHeader left, CacheControlHeader right)
        => left.Version == right.Version && left.Operation == right.Operation
            && left.ScopeHash.FixedTimeEquals(right.ScopeHash)
            && left.PolicyHash.FixedTimeEquals(right.PolicyHash)
            && left.PolicyRevision == right.PolicyRevision && left.OriginSlot == right.OriginSlot
            && left.TargetSlot == right.TargetSlot && left.CoordinatorSessionId == right.CoordinatorSessionId
            && left.RoundNonce == right.RoundNonce && left.RequestNonce == right.RequestNonce
            && left.SentUnixMilliseconds == right.SentUnixMilliseconds;
}
