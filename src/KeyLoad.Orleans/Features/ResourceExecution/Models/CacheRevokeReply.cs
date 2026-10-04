namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.RevokeReply), Immutable]
internal sealed record CacheRevokeReply(
    [property: Id(0)] CacheReplyCorrelation? Correlation,
    [property: Id(1)] CacheControlStatus Status,
    [property: Id(2)] Guid GrantId,
    [property: Id(3)] CacheRevokeEffect Effect,
    [property: Id(4)] CacheControlDigest Mac) : ICacheControlReply;
