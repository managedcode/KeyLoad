namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>Immutable acceptance and helper identity used as one read-admission epoch.</summary>
internal sealed record ZoneTreePointCacheBinding(
    CacheReadPermitAcceptance Acceptance,
    ZoneTreePointCache Cache,
    bool Retired);
