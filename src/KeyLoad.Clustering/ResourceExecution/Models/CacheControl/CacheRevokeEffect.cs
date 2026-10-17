namespace KeyLoad.Clustering.Features.ResourceExecution;

internal enum CacheRevokeEffect : byte
{
    None = 0,
    PendingRemoved = 1,
    LeaseWithdrawn = 2,
    Both = 3
}
