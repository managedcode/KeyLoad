namespace KeyLoad.Clustering.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.PrepareReply), Immutable]
internal sealed record CachePrepareReply(
    [property: Id(0)] CacheReplyCorrelation? Correlation,
    [property: Id(1)] CacheControlStatus Status,
    [property: Id(2)] CacheReadyProof? Proof,
    [property: Id(3)] CacheControlDigest Mac) : ICacheControlReply;
