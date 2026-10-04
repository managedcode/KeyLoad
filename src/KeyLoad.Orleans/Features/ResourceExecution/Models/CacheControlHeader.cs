namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.Header), Immutable]
internal sealed record CacheControlHeader(
    [property: Id(0)] byte Version,
    [property: Id(1)] CacheControlOperation Operation,
    [property: Id(2)] CacheControlDigest ScopeHash,
    [property: Id(3)] CacheControlDigest PolicyHash,
    [property: Id(4)] long PolicyRevision,
    [property: Id(5)] CacheVoterSlot OriginSlot,
    [property: Id(6)] CacheVoterSlot? TargetSlot,
    [property: Id(7)] Guid CoordinatorSessionId,
    [property: Id(8)] Guid RoundNonce,
    [property: Id(9)] Guid RequestNonce,
    [property: Id(10)] long SentUnixMilliseconds);
