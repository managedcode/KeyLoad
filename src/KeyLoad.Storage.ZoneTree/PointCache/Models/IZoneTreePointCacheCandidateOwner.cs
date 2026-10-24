namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

internal interface IZoneTreePointCacheCandidateOwner
{
    void DisposeCandidate(ZoneTreePointCacheCandidate candidate);
}
