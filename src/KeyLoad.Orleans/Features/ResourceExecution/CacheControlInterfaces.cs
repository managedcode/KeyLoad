namespace KeyLoad.Orleans.Features.ResourceExecution;

internal interface ICacheControlMessage
{
    CacheControlDigest Mac { get; }
}

internal interface ICacheControlRequest : ICacheControlMessage
{
    CacheControlHeader Header { get; }
}

internal interface ICacheControlReply : ICacheControlMessage
{
    CacheReplyCorrelation? Correlation { get; }
}
