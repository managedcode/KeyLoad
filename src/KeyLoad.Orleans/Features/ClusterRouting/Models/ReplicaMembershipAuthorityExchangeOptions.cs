namespace KeyLoad.Orleans;

internal sealed record ReplicaMembershipAuthorityExchangeOptions(
    string ClusterId,
    Guid AuthorityPhysicalShardId,
    Guid AuthorityIncarnation,
    Guid CallerPhysicalShardId,
    Guid CallerIncarnation,
    string CallerVoterId,
    string CallerSiloAddress,
    IReadOnlyList<Uri> AuthorityEndpoints,
    ReadOnlyMemory<byte> CallerPeerSecret,
    ReadOnlyMemory<byte> AuthorityPeerSecret,
    TimeProvider Clock);
