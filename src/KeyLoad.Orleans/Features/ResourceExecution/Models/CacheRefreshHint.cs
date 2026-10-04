namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.RefreshRequest), Immutable]
internal sealed record CacheRefreshHint(
    [property: Id(0)] CacheControlHeader Header,
    [property: Id(1)] CachePhysicalBinding SenderBinding,
    [property: Id(2)] CacheControlDigest Mac) : ICacheControlRequest;
