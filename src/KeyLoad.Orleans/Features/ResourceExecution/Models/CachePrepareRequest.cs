namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.PrepareRequest), Immutable]
internal sealed record CachePrepareRequest(
    [property: Id(0)] CacheControlHeader Header,
    [property: Id(1)] string ExpectedTargetSiloAddress,
    [property: Id(2)] CacheControlDigest Mac) : ICacheControlRequest;
