namespace KeyLoad.Clustering.Features.ResourceExecution;

internal interface ICacheControlRequest : ICacheControlMessage
{
    CacheControlHeader Header
    {
        get;
    }
}
