namespace KeyLoad.Clustering.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.ReplyCorrelation), Immutable]
internal sealed record CacheReplyCorrelation(
    [property: Id(0)] CacheControlHeader Header,
    [property: Id(1)] CacheControlDigest SignedRequestDigest);
