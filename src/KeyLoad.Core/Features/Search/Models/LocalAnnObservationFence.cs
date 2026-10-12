namespace KeyLoad.Core;

internal sealed record LocalAnnObservationFence(string PrincipalId, string TenantId, long PolicyEpoch,
    Guid NodeId, long ReadGeneration, string ResourceSha256, string ConsumerSha256);
