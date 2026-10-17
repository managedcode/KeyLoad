namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Mutable standard-provider configuration; this record never grants native owner authority.</summary>
[ConfigurationOptions]
internal sealed class ClusterRestoreMappingConfiguration
{
    public ClusterRestorePhysicalOwnerConfiguration Source { get; set; } = new();
    public ClusterRestorePhysicalOwnerConfiguration Target { get; set; } = new();
    public string[] Endpoints { get; set; } = [];

    internal ClusterRestoreOwnerMapping ToNative() => new(ClusterRestoreOwnerMapping.CurrentVersion,
        Source.ToNative(), Target.ToNative(), [.. Endpoints]);
}

