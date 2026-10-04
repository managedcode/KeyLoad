namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.ReadyProof), Immutable]
internal sealed record CacheReadyProof(
    [property: Id(0)] byte Version,
    [property: Id(1)] CacheControlDigest ScopeHash,
    [property: Id(2)] CacheControlDigest PolicyHash,
    [property: Id(3)] long PolicyRevision,
    [property: Id(4)] CacheVoterSlot OriginSlot,
    [property: Id(5)] Guid CoordinatorSessionId,
    [property: Id(6)] Guid RoundNonce,
    [property: Id(7)] Guid ChallengeId,
    [property: Id(8)] long ChallengeSequence,
    [property: Id(9)] CachePhysicalBinding Binding,
    [property: Id(10)] CacheControlStatus Status,
    [property: Id(11)] CacheControlDigest Mac) : ICacheControlMessage;
