namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Configured physical database groups, independent of the native Orleans membership provider.</summary>
[ConfigurationOptions]
internal sealed record PhysicalGroupTrustOptions
{
    public PhysicalGroupTrustRole Role { get; init; }
    public bool RemotePartitionQueries { get; init; }
    public bool RemoteDocumentReads { get; init; }
    public bool RegisterPhysicalOwners { get; init; }
    public Guid ForeignPhysicalShardId { get; init; }
    public Guid ForeignIncarnation { get; init; }
    public string[] ForeignVoterIds { get; init; } = [];
    public string? ForeignPeerSecret { get; init; }
}
