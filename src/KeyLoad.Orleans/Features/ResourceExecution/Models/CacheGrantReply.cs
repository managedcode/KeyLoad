namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.GrantReply), Immutable]
internal sealed record CacheGrantReply(
    [property: Id(0)] CacheReplyCorrelation? Correlation,
    [property: Id(1)] CacheControlStatus Status,
    [property: Id(2)] Guid GrantId,
    [property: Id(3)] CachePhysicalBinding? AcceptedBinding,
    [property: Id(4)] long AcceptedSequence,
    [property: Id(5)] CacheControlDigest Mac) : ICacheControlReply;
