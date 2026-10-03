namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.RevokeRequest), Immutable]
internal sealed record CacheRevokeRequest(
    [property: Id(0)] CacheControlHeader Header,
    [property: Id(1)] Guid GrantId,
    [property: Id(2)] CachePhysicalBinding TargetBinding,
    [property: Id(3)] CacheControlDigest Mac) : ICacheControlRequest;
