namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.GrantRequest), Immutable]
internal sealed record CacheGrantRequest(
    [property: Id(0)] CacheControlHeader Header,
    [property: Id(1)] Guid GrantId,
    [property: Id(2)] CachePhysicalBinding TargetBinding,
    [property: Id(3)] CacheReadyProof Slot0Proof,
    [property: Id(4)] CacheReadyProof Slot1Proof,
    [property: Id(5)] CacheReadyProof Slot2Proof,
    [property: Id(6)] CacheControlDigest Mac) : ICacheControlRequest;
