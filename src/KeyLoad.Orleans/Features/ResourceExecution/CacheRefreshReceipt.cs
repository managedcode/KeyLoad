namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.RefreshReply), Immutable]
internal sealed record CacheRefreshReceipt(
    [property: Id(0)] CacheReplyCorrelation? Correlation,
    [property: Id(1)] CacheControlStatus Status,
    [property: Id(2)] Guid ActualCoordinatorSessionId,
    [property: Id(3)] Guid CurrentRoundNonce,
    [property: Id(4)] string? ActualCoordinatorSiloAddress,
    [property: Id(5)] CacheControlDigest Mac) : ICacheControlReply;
