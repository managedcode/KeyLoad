namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Explicit configured scalar tuple converted once, before actual native mapping validation.</summary>
[ConfigurationOptions]
internal sealed class ClusterRestorePhysicalOwnerConfiguration
{
    public Guid PhysicalShardId { get; set; }
    public Guid Incarnation { get; set; }
    public string[] VoterIds { get; set; } = [];
    public long PlacementEpoch { get; set; }

    internal PhysicalShardRecord ToNative() => new(PhysicalShardId, Incarnation, [.. VoterIds], PlacementEpoch);
}
