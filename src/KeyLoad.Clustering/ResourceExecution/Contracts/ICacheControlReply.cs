namespace KeyLoad.Clustering.Features.ResourceExecution;

internal interface ICacheControlReply : ICacheControlMessage
{
    CacheReplyCorrelation? Correlation
    {
        get;
    }
}
