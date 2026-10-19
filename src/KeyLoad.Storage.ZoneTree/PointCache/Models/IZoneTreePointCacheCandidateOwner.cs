namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

internal interface IZoneTreePointCacheCandidateOwner
{
    void DisposeCandidate(ZoneTreePointCacheCandidate candidate);
}
