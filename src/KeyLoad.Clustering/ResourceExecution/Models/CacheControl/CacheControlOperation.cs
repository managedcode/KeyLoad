namespace KeyLoad.Clustering.Features.ResourceExecution;

internal enum CacheControlOperation : byte
{
    Prepare = 1,
    Grant = 2,
    Revoke = 3,
    Refresh = 4
}
